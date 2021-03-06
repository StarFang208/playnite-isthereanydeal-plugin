﻿using AngleSharp.Dom.Html;
using AngleSharp.Parser.Html;
using IsThereAnyDeal.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Playnite.SDK;
using PluginCommon;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace IsThereAnyDeal.Services
{
    class IsThereAnyDealApi
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly string baseAddress = "https://api.isthereanydeal.com/";
        private readonly string key = "fa49308286edcaf76fea58926fd2ea2d216a17ff";


        private async Task<string> DownloadStringData(string url)
        {
            string responseData = string.Empty;

            try
            {
                using (var client = new HttpClient())
                {
                    responseData = await client.GetStringAsync(url).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "IsThereAnyDeal", $"Failed to download {url}");
            }

            return responseData;
        }


        public List<Wishlist> LoadWishlist(IsThereAnyDeal plugin, IPlayniteAPI PlayniteApi, IsThereAnyDealSettings settings, string PluginUserDataPath, bool CacheOnly = false, bool Force = false)
        {
            Guid SteamId = new Guid();
            Guid GogId = new Guid();
            Guid EpicId = new Guid();
            Guid HumbleId = new Guid();

            foreach (var Source in PlayniteApi.Database.Sources)
            {
                if (Source.Name.ToLower() == "steam")
                {
                    SteamId = Source.Id;
                }

                if (Source.Name.ToLower() == "gog")
                {
                    GogId = Source.Id;
                }

                if (Source.Name.ToLower() == "epic")
                {
                    EpicId = Source.Id;
                }

                if (Source.Name.ToLower() == "humble")
                {
                    HumbleId = Source.Id;
                }
            }


            List<Wishlist> ListWishlistSteam = new List<Wishlist>();
            if (settings.EnableSteam)
            {
                if (!Tools.IsDisabledPlaynitePlugins("SteamLibrary", PluginUserDataPath))
                {
                    SteamWishlist steamWishlist = new SteamWishlist();
                    ListWishlistSteam = steamWishlist.GetWishlist(PlayniteApi, SteamId, PluginUserDataPath, settings, CacheOnly, Force);
                }
                else
                {
                    logger.Warn("IsThereAnyDeal - Steam is enable then disabled");
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"IsThereAnyDeal-Steam-disabled",
                        "Steam is enable then disabled",
                        NotificationType.Error,
                        () => plugin.OpenSettingsView()
                    ));
                }
            }

            List<Wishlist> ListWishlistGog = new List<Wishlist>();
            if (settings.EnableGog)
            {
                if (!Tools.IsDisabledPlaynitePlugins("GogLibrary", PluginUserDataPath))
                {
                    GogWishlist gogWishlist = new GogWishlist(PlayniteApi);
                    ListWishlistGog = gogWishlist.GetWishlist(PlayniteApi, GogId, PluginUserDataPath, settings, CacheOnly, Force);
                }
                else
                {
                    logger.Warn("IsThereAnyDeal - GOG is enable then disabled");
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"IsThereAnyDeal-GOG-disabled",
                        "GOG is enable then disabled",
                        NotificationType.Error,
                        () => plugin.OpenSettingsView()
                    ));
                }
            }

            List<Wishlist> ListWishlistEpic = new List<Wishlist>();
            if (settings.EnableEpic)
            {
                if (!Tools.IsDisabledPlaynitePlugins("EpicLibrary", PluginUserDataPath))
                {
                    EpicWishlist epicWishlist = new EpicWishlist();
                    ListWishlistEpic = epicWishlist.GetWishlist(PlayniteApi, GogId, PluginUserDataPath, settings, CacheOnly, Force);
                }
                else
                {
                    logger.Warn("IsThereAnyDeal - Epic Game Store is enable then disabled");
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"IsThereAnyDeal-EpicGameStore-disabled",
                        "Epic Game Store is enable then disabled",
                        NotificationType.Error,
                        () => plugin.OpenSettingsView()
                    ));
                }
            }

            List<Wishlist> ListWishlistHumble = new List<Wishlist>();
            if (settings.EnableHumble)
            {
                if (!Tools.IsDisabledPlaynitePlugins("HumbleLibrary", PluginUserDataPath))
                {
                    HumbleBundleWishlist humbleBundleWishlist = new HumbleBundleWishlist();
                    ListWishlistHumble = humbleBundleWishlist.GetWishlist(PlayniteApi, HumbleId, settings.HumbleKey, PluginUserDataPath, settings, CacheOnly, Force);
                }
                else
                {
                    logger.Warn("IsThereAnyDeal - Humble Bundle is enable then disabled");
                    PlayniteApi.Notifications.Add(new NotificationMessage(
                        $"IsThereAnyDeal-HumbleBundle-disabled",
                        "Humble Bundle is enable then disabled",
                        NotificationType.Error,
                        () => plugin.OpenSettingsView()
                    ));
                }
            }

            List<Wishlist> ListWishlist = ListWishlistSteam.Concat(ListWishlistGog).Concat(ListWishlistHumble)
                .Concat(ListWishlistEpic).ToList();


            // Group same game
            var listDuplicates = ListWishlist.GroupBy(c => c.Name.ToLower()).Where(g => g.Skip(1).Any());
            foreach (var duplicates in listDuplicates)
            {
                bool isFirst = true;
                Wishlist keep = new Wishlist();
                foreach(var wish in duplicates)
                {
                    if (isFirst)
                    {
                        keep = wish;
                        isFirst = false;
                    }
                    else
                    {
                        List<Wishlist> keepDuplicates = keep.Duplicates;
                        keepDuplicates.Add(wish);
                        keep.Duplicates = keepDuplicates;

                        ListWishlist.Find(x => x == keep).Duplicates = keepDuplicates;
                        ListWishlist.Find(x => x == keep).hasDuplicates = true;
                        ListWishlist.Remove(wish);
                    }
                }
            }

            return ListWishlist.OrderBy(wishlist => wishlist.Name).ToList();
        }


        public List<ItadRegion> GetCoveredRegions()
        {
            List<ItadRegion> itadRegions = new List<ItadRegion>();
            try
            {
                string responseData = DownloadStringData(baseAddress + "v01/web/regions/").GetAwaiter().GetResult();

                JObject datasObj = JObject.Parse(responseData);
                if (((JObject)datasObj["data"]).Count > 0)
                {
                    foreach (var dataObj in ((JObject)datasObj["data"]))
                    {
                        List<string> countries = new List<string>();
                        foreach (string country in ((JArray)dataObj.Value["countries"]))
                        {
                            countries.Add(country);
                        }

                        itadRegions.Add(new ItadRegion
                        {
                            Region = dataObj.Key,
                            CurrencyName = (string)dataObj.Value["currency"]["name"],
                            CurrencyCode = (string)dataObj.Value["currency"]["code"],
                            CurrencySign = (string)dataObj.Value["currency"]["sign"],
                            Countries = countries
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "IsThereAnyDeal", "Error to parse downloaded data in GetCoveredRegions()");
            }

            return itadRegions;
        }

        public List<ItadStore> GetRegionStores(string region, string country)
        {
            List<ItadStore> RegionStores = new List<ItadStore>();
            try
            {
                string url = baseAddress + $"v02/web/stores/?region={region}&country={country}";
                string responseData = DownloadStringData(url).GetAwaiter().GetResult();

                JObject datasObj = JObject.Parse(responseData);
                if (((JArray)datasObj["data"]).Count > 0)
                {
                    RegionStores = JsonConvert.DeserializeObject<List<ItadStore>>((JsonConvert.SerializeObject((JArray)datasObj["data"])));
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "IsThereAnyDeal", "Error to parse downloaded data in GetRegionStores()");
            }

            return RegionStores;
        }

        public string GetPlain(string title)
        {
            string Plain = string.Empty;
            try
            {
                string url = baseAddress + $"v02/game/plain/?key={key}&title={WebUtility.UrlEncode(WebUtility.HtmlDecode(title))}";
                string responseData = DownloadStringData(url).GetAwaiter().GetResult();

                JObject datasObj = JObject.Parse(responseData);
                if ((string)datasObj[".meta"]["match"] != "false")
                {
                    Plain = (string)datasObj["data"]["plain"];
                }
                else
                {
                    logger.Warn($"IsThereAnyDeal - not find for {WebUtility.HtmlDecode(title)}");
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "IsThereAnyDeal", $"Error in GetPlain({WebUtility.HtmlDecode(title)})");
            }

            return Plain;
        }

        public List<ItadGameInfo> SearchGame(string q, string region, string country)
        {
#if DEBUG
            logger.Debug($"IsThereAnyDeal - SearchGame({q})");
#endif

            List<ItadGameInfo> itadGameInfos = new List<ItadGameInfo>();
            try
            {
                string url = baseAddress + $"v01/search/search/?key={key}&q={q}&region{region}&country={country}";
                string responseData = DownloadStringData(url).GetAwaiter().GetResult();

                JObject datasObj = JObject.Parse(responseData);
                if (((JArray)datasObj["data"]["list"]).Count > 0)
                {
                    foreach (JObject dataObj in ((JArray)datasObj["data"]["list"]))
                    {
                        itadGameInfos.Add(new ItadGameInfo
                        {
                            Plain = (string)dataObj["plain"],
                            //title = (string)dataObj["title"],
                            PriceNew = (double)dataObj["price_new"],
                            PriceOld = (double)dataObj["price_old"],
                            PriceCut = (double)dataObj["price_cut"],
                            //added = new DateTime(1970, 1, 1, 0, 0, 0, 0).AddSeconds((int)dataObj["added"]),
                            ShopName = (string)dataObj["shop"]["name"],
                            //shop_color = GetShopColor((string)dataObj["shop"]["name"], settings.Stores),
                            UrlBuy = (string)dataObj["urls"]["buy"]
                            //url_game = (string)dataObj["urls"]["game"],
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "IsThereAnyDeal", "Error in SearchGame()");
            }

            return itadGameInfos;
        }

        public List<Wishlist> GetCurrentPrice(List<Wishlist> wishlists, IsThereAnyDealSettings settings, IPlayniteAPI PlayniteApi)
        {
            // IS allready load?
            if (wishlists.Count > 0)
            {
                foreach(Wishlist wishlist in wishlists)
                {
                    if (wishlist.itadGameInfos != null && wishlist.itadGameInfos.Keys.Contains(DateTime.Now.ToString("yyyy-MM-dd")))
                    {
#if DEBUG
                        logger.Debug("IsThereAnyDeal - Current price is allready load");
#endif

                        return wishlists;
                    }
                }
            }


            List<Wishlist> Result = new List<Wishlist>();

            string plains = string.Empty;
            foreach (Wishlist wishlist in wishlists)
            {
                if (plains == string.Empty)
                {
                    plains += wishlist.Plain;
                }
                else
                {
                    plains += "," + wishlist.Plain;
                }
            }
#if DEBUG
            logger.Debug($"IsThereAnyDeal - GetCurrentPrice({plains})");
#endif

            string shops = string.Empty;
            foreach (ItadStore Store in settings.Stores)
            {
                if (Store.IsCheck)
                {
                    if (shops == string.Empty)
                    {
                        shops += Store.Id;
                    }
                    else
                    {
                        shops += "," + Store.Id;
                    }
                }
            }

            if (!plains.IsNullOrEmpty())
            {
                try
                {
                    string url = baseAddress + $"v01/game/prices/?key={key}&plains={plains}&region{settings.Region}&country={settings.Country}&shops={shops}";
                    string responseData = DownloadStringData(url).GetAwaiter().GetResult();

                    foreach (Wishlist wishlist in wishlists)
                    {
                        ConcurrentDictionary<string, List<ItadGameInfo>> itadGameInfos = new ConcurrentDictionary<string, List<ItadGameInfo>>();
                        List<ItadGameInfo> dataCurrentPrice = new List<ItadGameInfo>();
                        JObject datasObj = JObject.Parse(responseData);

                        // Check if in library (exclude game emulated)
                        List<Guid> ListEmulators = new List<Guid>();
                        foreach (var item in PlayniteApi.Database.Emulators)
                        {
                            ListEmulators.Add(item.Id);
                        }

                        bool InLibrary = false;
                        foreach (var game in PlayniteApi.Database.Games.Where(a => a.Name.ToLower() == wishlist.Name.ToLower()))
                        {
                            if (game.PlayAction != null && game.PlayAction.EmulatorId != null && ListEmulators.Contains(game.PlayAction.EmulatorId))
                            {
                                InLibrary = false;
                            }
                            else
                            {
                                InLibrary = true;
                            }        
                        }
                        wishlist.InLibrary = InLibrary;


                        if (((JArray)datasObj["data"][wishlist.Plain]["list"]).Count > 0)
                        {
                            foreach (JObject dataObj in ((JArray)datasObj["data"][wishlist.Plain]["list"]))
                            {
                                dataCurrentPrice.Add(new ItadGameInfo
                                {
                                    Plain = wishlist.Plain,
                                    PriceNew = Math.Round((double)dataObj["price_new"], 2),
                                    PriceOld = Math.Round((double)dataObj["price_old"], 2),
                                    PriceCut = (double)dataObj["price_cut"],
                                    CurrencySign = settings.CurrencySign,
                                    ShopName = (string)dataObj["shop"]["name"],
                                    ShopColor = GetShopColor((string)dataObj["shop"]["name"], settings.Stores),
                                    UrlBuy = (string)dataObj["url"]
                                });
                            }
                        }
                        itadGameInfos.TryAdd(DateTime.Now.ToString("yyyy-MM-dd"), dataCurrentPrice);

                        wishlist.itadGameInfos = itadGameInfos;
                        Result.Add(wishlist);
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, "IsThereAnyDeal", $"Error in GetCurrentPrice({plains})");
                }
            }
            else
            {
#if DEBUG
                logger.Debug("IsThereAnyDeal - No plain");
#endif
            }

            return Result;
        }

        private string GetShopColor(string ShopName, List<ItadStore> itadStores)
        {
            foreach (ItadStore store in itadStores)
            {
                if (ShopName == store.Title)
                {
                    return store.Color;
                }
            }
            return null;
        }


        public List<ItadGiveaway> GetGiveaways(IPlayniteAPI PlayniteApi, string PluginUserDataPath, bool CacheOnly = false)
        {
            // Load previous
            string PluginDirectoryCache = PluginUserDataPath + "\\cache";
            string PluginFileCache = PluginDirectoryCache + "\\giveways.json";
            List<ItadGiveaway> itadGiveawaysCache = new List<ItadGiveaway>();
            try
            {
                if (!Directory.Exists(PluginDirectoryCache))
                {
                    Directory.CreateDirectory(PluginDirectoryCache);
                }
                
                if (File.Exists(PluginFileCache))
                {
                    string fileData = File.ReadAllText(PluginFileCache);
                    itadGiveawaysCache = JsonConvert.DeserializeObject<List<ItadGiveaway>>(fileData);
                }
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "IsThereAnyDeal", "Error in GetGiveAway() with cache data");
            }


            // Load on web
            List<ItadGiveaway> itadGiveaways = new List<ItadGiveaway>();
            if (!CacheOnly && itadGiveawaysCache != new List<ItadGiveaway>())
            {
                string url = @"https://isthereanydeal.com/specials/#/filter:&giveaway,&active";
                try
                {
                    string responseData = DownloadStringData(url).GetAwaiter().GetResult();

                    if (responseData != string.Empty)
                    {
                        HtmlParser parser = new HtmlParser();
                        IHtmlDocument htmlDocument = parser.Parse(responseData);
                        foreach (var SearchElement in htmlDocument.QuerySelectorAll("div.giveaway"))
                        {
                            bool HasSeen = (SearchElement.ClassName.IndexOf("Seen") > -1);

                            var row1 = SearchElement.QuerySelector("div.bundle-row1");

                            DateTime? bundleTime = null;
                            if (!row1.QuerySelector("div.bundle-time").GetAttribute("title").IsNullOrEmpty())
                            {
                                bundleTime = Convert.ToDateTime(row1.QuerySelector("div.bundle-time").GetAttribute("title"));
                            }

                            string TitleAll = row1.QuerySelector("div.bundle-title a").InnerHtml.Trim();

                            List<string> arrBundleTitle = TitleAll.Split('-').ToList();

                            string bundleShop = arrBundleTitle[arrBundleTitle.Count - 1].Trim();
                            bundleShop = bundleShop.Replace("FREE Games on", string.Empty).Replace("Always FREE For", string.Empty)
                                .Replace("FREE For", string.Empty).Replace("FREE on", string.Empty);

                            string bundleTitle = string.Empty;
                            arrBundleTitle.RemoveAt(arrBundleTitle.Count - 1);
                            bundleTitle = String.Join("-", arrBundleTitle.ToArray()).Trim();

                            string bundleLink = row1.QuerySelector("div.bundle-title a").GetAttribute("href");

                            var row2 = SearchElement.QuerySelector("div.bundle-row2");

                            string bundleDescCount = row2.QuerySelector("div.bundle-desc span.lg").InnerHtml;

                            itadGiveaways.Add(new ItadGiveaway
                            {
                                TitleAll = TitleAll,
                                Title = bundleTitle,
                                Time = bundleTime,
                                Link = bundleLink,
                                ShopName = bundleShop,
                                Count = bundleDescCount,
                                HasSeen = HasSeen
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Common.LogError(ex, "IsThereAnyDeal", "Error in GetGiveAway() with web data");
                }
            }

            // Compare new with cache
            if (itadGiveaways.Count != 0)
            {
#if DEBUG
                logger.Debug("IsThereAnyDeal - Compare with cache");
#endif
                foreach (ItadGiveaway itadGiveaway in itadGiveawaysCache)
                {
                    if (itadGiveaways.Find(x => x.TitleAll == itadGiveaway.TitleAll) != null)
                    {
                        itadGiveaways.Find(x => x.TitleAll == itadGiveaway.TitleAll).HasSeen = true;
                    }
                }
            }
            // No data
            else
            {
                logger.Warn("IsThereAnyDeal - No new data for GetGiveaways()");
                itadGiveaways = itadGiveawaysCache;
            }

            // Save new
            try
            {
                File.WriteAllText(PluginFileCache, JsonConvert.SerializeObject(itadGiveaways));
            }
            catch (Exception ex)
            {
                Common.LogError(ex, "IsThereAnyDeal", "Error in GetGiveAway() with save data");
            }

            return itadGiveaways;
        }
    }
}
