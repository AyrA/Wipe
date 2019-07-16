namespace Wipe
{
    public static class Tools
    {
        public static string StrOrDefault(object o, string Default = "")
        {
            if (o == null || string.IsNullOrEmpty(o.ToString()))
            {
                return Default;
            }
            return o.ToString();
        }

        public static int IntOrDefault(object o, int Default = 0)
        {
            int v = 0;
            if (o == null)
            {
                return Default;
            }
            if (int.TryParse(o.ToString(), out v))
            {
                return v;
            }
            return Default;
        }
    }
}
