using System;
using System.Configuration;

namespace CodeRunner
{
    // Reusing key/value typed collection elements from CsrSection

    internal class ScaffoldSection : ConfigurationSection
    {
        [ConfigurationProperty("settings", IsDefaultCollection = true)]
        public TypeElementCollection Settings
        {
            get { return (TypeElementCollection)(base["settings"]); }
        }
    }
}