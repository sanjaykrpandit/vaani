using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vaani.Services
{
    public static class AppVersionHelper
    {
        public static string GetAppVersion()
        {
            //var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            //return version != null ? version.ToString() : "Unknown";

            return "1.0.0.34"; // Placeholder version
        }
    }
}
