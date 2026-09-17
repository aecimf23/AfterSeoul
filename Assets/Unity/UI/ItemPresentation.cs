using AfterSeoul.Core;

namespace AfterSeoul.Unity.UI
{
    internal static class ItemPresentation
    {
        internal static string Name(IDataRegistry data, string id)
        {
            string key = "ITEM_" + id + "_NAME";
            if (Loc.Has(key)) return Loc.Get(key);
            string name = data.GetItem(id)?.ShortName;
            return string.IsNullOrEmpty(name) ? id : name;
        }
    }
}
