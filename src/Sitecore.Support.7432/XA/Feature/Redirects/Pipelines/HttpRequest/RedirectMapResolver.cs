namespace Sitecore.Support.XA.Feature.Redirects.Pipelines.HttpRequest
{
    using Sitecore;
    using Sitecore.Data.Items;
    using Sitecore.Diagnostics;
    using Sitecore.Pipelines.HttpRequest;
    using Sitecore.Text;
    using Sitecore.Web;
    using Sitecore.XA.Feature.Redirects.Pipelines.HttpRequest;
    using Sitecore.XA.Foundation.IoC;
    using Sitecore.XA.Foundation.Multisite;
    using Sitecore.XA.Foundation.SitecoreExtensions.Comparers;
    using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Web;
    using System.Web.Caching;

    public class RedirectMapResolver : HttpRequestProcessor
    {
        private string EnsureSlashes(string text) =>
            StringUtil.EnsurePostfix('/', StringUtil.EnsurePrefix('/', text));

        protected virtual RedirectMapping FindMapping(string filePath)
        {
            foreach (RedirectMapping mapping in this.MappingsMap)
            {
                if (!mapping.IsRegex && (mapping.Pattern == filePath))
                {
                    return mapping;
                }
                if (mapping.IsRegex && mapping.Regex.IsMatch(filePath))
                {
                    return mapping;
                }
            }
            return null;
        }

        protected virtual RedirectMapping GetResolvedMapping(string filePath)
        {
            Dictionary<string, RedirectMapping> dictionary = HttpRuntime.Cache[this.ResolvedMappingsPrefix] as Dictionary<string, RedirectMapping>;
            if ((dictionary != null) && dictionary.ContainsKey(filePath))
            {
                return dictionary[filePath];
            }
            return null;
        }

        protected virtual string GetTargetUrl(RedirectMapping mapping, string input)
        {
            string target = mapping.Target;
            if (mapping.IsRegex)
            {
                target = mapping.Regex.Replace(input, target);
            }
            if (mapping.PreserveQueryString)
            {
                target = target + HttpContext.Current.Request.Url.Query;
            }
            if (!string.IsNullOrEmpty(Context.Site.VirtualFolder))
            {
                char[] trimChars = new char[] { '/' };
                target = StringUtil.EnsurePostfix('/', Context.Site.VirtualFolder) + target.TrimStart(trimChars);
            }
            return target;
        }

        protected virtual bool IsFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && !WebUtil.IsExternalUrl(filePath))
            {
                return File.Exists(HttpContext.Current.Server.MapPath(filePath));
            }
            return true;
        }

        public override void Process(HttpRequestArgs args)
        {
            if (((Context.Item == null) && (Context.Database != null)) && ((Context.Site != null) && !this.IsFile(Context.Request.FilePath)))
            {
                string filePath = this.EnsureSlashes(Context.Request.FilePath.ToLower());
                RedirectMapping resolvedMapping = this.GetResolvedMapping(filePath);
                bool flag = resolvedMapping != null;
                if (resolvedMapping == null)
                {
                    resolvedMapping = this.FindMapping(filePath);
                }
                if ((resolvedMapping != null) && !flag)
                {
                    Dictionary<string, RedirectMapping> dictionary = (HttpRuntime.Cache[this.ResolvedMappingsPrefix] as Dictionary<string, RedirectMapping>) ?? new Dictionary<string, RedirectMapping>();
                    dictionary[filePath] = resolvedMapping;
                    HttpRuntime.Cache.Add(this.ResolvedMappingsPrefix, dictionary, null, DateTime.UtcNow.AddMinutes((double)this.CacheExpiration), TimeSpan.Zero, CacheItemPriority.Normal, null);
                }
                if ((resolvedMapping != null) && (HttpContext.Current != null))
                {
                    string targetUrl = this.GetTargetUrl(resolvedMapping, filePath);
                    if (resolvedMapping.RedirectType == RedirectType.Redirect301)
                    {
                        this.Redirect301(HttpContext.Current.Response, targetUrl);
                    }
                    if (resolvedMapping.RedirectType == RedirectType.Redirect302)
                    {
                        HttpContext.Current.Response.Redirect(targetUrl, true);
                    }
                    if (resolvedMapping.RedirectType == RedirectType.ServerTransfer)
                    {
                        HttpContext.Current.Server.TransferRequest(targetUrl);
                    }
                }
            }
        }

        protected virtual void Redirect301(HttpResponse response, string url)
        {
            HttpCookieCollection cookies = new HttpCookieCollection();
            for (int i = 0; i < response.Cookies.Count; i++)
            {
                HttpCookie cookie = response.Cookies[i];
                if (cookie != null)
                {
                    cookies.Add(cookie);
                }
            }
            response.Clear();
            for (int j = 0; j < cookies.Count; j++)
            {
                HttpCookie cookie2 = cookies[j];
                if (cookie2 != null)
                {
                    response.Cookies.Add(cookie2);
                }
            }
            response.Status = "301 Moved Permanently";
            response.AddHeader("Location", url);
            response.End();
        }

        private string AllMappingsPrefix =>
            $"{"SXA-Redirect-"}AllMappings-{Context.Database.Name}-{Context.Site.Name}";

        public int CacheExpiration { get; set; }

        protected virtual List<RedirectMapping> MappingsMap
        {
            get
            {
                List<RedirectMapping> list = HttpRuntime.Cache[this.AllMappingsPrefix] as List<RedirectMapping>;
                if (list == null)
                {
                    list = new List<RedirectMapping>();
                    Item item = ServiceLocator.Current.Resolve<IMultisiteContext>().GetSettingsItem(Context.Database.GetItem(Context.Site.StartPath)).FirstChildInheritingFrom((ServiceLocator.Current.Resolve<IMultisiteContext>().GetSettingsItem(Context.Database.GetItem(Context.Site.StartPath)) == null) ? null : Sitecore.XA.Feature.Redirects.Templates.RedirectMapGrouping.ID);
                    if (item != null)
                    {
                        #region Changed code
                        // get descendants instead of children
                        Item[] array = (from i in item.Axes.GetDescendants() where i.InheritsFrom(Sitecore.XA.Feature.Redirects.Templates.RedirectMap.ID) select i).ToArray(); 
                        #endregion
                        Array.Sort<Item>(array, new TreeComparer());
                        foreach (Item item2 in array)
                        {
                            RedirectType type;
                            if (!Enum.TryParse<RedirectType>(item2[Sitecore.XA.Feature.Redirects.Templates.RedirectMap.Fields.RedirectType], out type))
                            {
                                Log.Info($"Redirect map {item2.Paths.FullPath} does not specify redirect type.", this);
                            }
                            else
                            {
                                bool @bool = MainUtil.GetBool(item2[Sitecore.XA.Feature.Redirects.Templates.RedirectMap.Fields.PreserveQueryString], false);
                                UrlString str = new UrlString
                                {
                                    Query = item2[Sitecore.XA.Feature.Redirects.Templates.RedirectMap.Fields.UrlMapping]
                                };
                                foreach (string str2 in str.Parameters.Keys)
                                {
                                    if (!string.IsNullOrEmpty(str2))
                                    {
                                        string str3 = str.Parameters[str2];
                                        if (!string.IsNullOrEmpty(str3))
                                        {
                                            string text = str2.ToLower();
                                            bool flag2 = text.StartsWith("^") && text.EndsWith("$");
                                            if (!flag2)
                                            {
                                                text = this.EnsureSlashes(text);
                                            }
                                            str3 = HttpUtility.UrlDecode(str3.ToLower()) ?? string.Empty;
                                            char[] trimChars = new char[] { '^' };
                                            char[] chArray2 = new char[] { '$' };
                                            str3 = str3.TrimStart(trimChars).TrimEnd(chArray2);
                                            RedirectMapping mapping1 = new RedirectMapping
                                            {
                                                RedirectType = type,
                                                PreserveQueryString = @bool,
                                                Pattern = text,
                                                Target = str3,
                                                IsRegex = flag2
                                            };
                                            list.Add(mapping1);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (this.CacheExpiration > 0)
                    {
                        HttpRuntime.Cache.Add(this.AllMappingsPrefix, list, null, DateTime.UtcNow.AddMinutes((double)this.CacheExpiration), TimeSpan.Zero, CacheItemPriority.Normal, null);
                    }
                }
                return list;
            }
        }

        private string ResolvedMappingsPrefix =>
            $"{"SXA-Redirect-"}ResolvedMappings-{Context.Database.Name}-{Context.Site.Name}";
    }
}