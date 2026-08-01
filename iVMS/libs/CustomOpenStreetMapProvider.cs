using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using GMap.NET.Entity;
using GMap.NET.MapProviders;
using GMap.NET;
using Newtonsoft.Json;
using System.Web.Caching;
using System.Net;

namespace V3SClient.libs
{
    public class CustomOpenStreetMapProvider : OpenStreetMapProviderBase
    {
        public static readonly CustomOpenStreetMapProvider Instance =new CustomOpenStreetMapProvider();

        private GMapProvider[] _overlays;

        private static readonly string UrlFormat;

        public override Guid Id { get; } = new Guid("12345678-90AB-CDEF-1234-567890ABCDEF");


        public override string Name { get; } = nameof(CustomOpenStreetMapProvider);


        public string YoursClientName { get; set; }

        public override GMapProvider[] Overlays
        {
            get
            {
                if (_overlays == null)
                {
                    _overlays = new GMapProvider[1] { this };
                }

                return _overlays;
            }
        }

        private CustomOpenStreetMapProvider()
        {
        }
        public override PureImage GetTileImage(GPoint pos, int zoom)
        {
            try
            {
                string baseUrl = ApiManager.Instance.MapUrl;
                if (string.IsNullOrEmpty(baseUrl))
                    baseUrl = "https://a.tile.Topenstreetmap.org";

                string url = baseUrl.TrimEnd('/');
                if (baseUrl.Contains("openstreetmap.org"))
                {
                    url = $"{url}/{zoom}/{pos.X}/{pos.Y}.png";
                }
                else
                {
                    // For local servers, prefer /tile/ prefix as it was working before
                    if (url.EndsWith("/tile")) 
                        url = $"{url}/{zoom}/{pos.X}/{pos.Y}.png";
                    else if (url.Contains(":8090") || url.Contains(":8080"))
                        url = $"{url}/tile/{zoom}/{pos.X}/{pos.Y}.png";
                    else
                        url = $"{url}/{zoom}/{pos.X}/{pos.Y}.png"; // Root
                }

                return GetTileImageUsingHttp(url);
            }
            catch { return null; }
        }

        protected override void InitializeWebRequest(WebRequest request)
        {
            base.InitializeWebRequest(request);
            if (!string.IsNullOrEmpty(YoursClientName))
            {
                request.Headers.Add("X-Yours-client", YoursClientName);
            }
        }

      
    }
}
















