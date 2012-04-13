using System;
using System.Configuration;

namespace CodeRunner
{
    internal class TypeElement : ConfigurationElement
    {
        [ConfigurationProperty("key", IsRequired = true, DefaultValue = "", IsKey = true)]
        public string Key
        {
            get { return (string)this["key"]; }
            set { this["key"] = value; }
        }

        [ConfigurationProperty("value", IsRequired = true, DefaultValue = "", IsKey = false)]
        public string Value
        {
            get { return (string)this["value"]; }
            set { this["value"] = value; }
        }
    }

    [ConfigurationCollection(typeof(TypeElement))]
    internal class TypeElementCollection : ConfigurationElementCollection
    {
        protected override ConfigurationElement CreateNewElement()
        {
            return new TypeElement();
        }

        protected override object GetElementKey(ConfigurationElement element)
        {
            return ((TypeElement)(element)).Key;
        }

        public void Add(TypeElement element) { this.BaseAdd(element); }
        public void Remove(string key) { this.BaseRemove(key); }
        public void Clear() { this.BaseClear(); }

        public TypeElement this[int index]
        {
            get { return (TypeElement)this.BaseGet(index); }
        }
    }

    internal class CsrSection : ConfigurationSection
    {
        [ConfigurationProperty("settings", IsDefaultCollection = true)]
        public TypeElementCollection Settings
        {
            get { return (TypeElementCollection)(base["settings"]); }
        }
    }
}