#if !ALTIUM17
using Altium.Controls;
#endif
using DXP;
using EasyEDA_Loader;
using System.Runtime.InteropServices;

namespace CSharpPlugin
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class PluginFactory
    {
        public object InvokePluginFactory(IClient client)
        {
#if !ALTIUM17
            if (!client.ProductInfo().SupportsUIFeature("NoGUI", false))
            {
                IUITheme uiTheme = (client as IUIThemeManager)?.CurrentUITheme();
                if (uiTheme != null)
                    Style.Init(uiTheme.GetHRID(), uiTheme.GetAttributeDictionary());
                else
                    Style.Init();

            }
#endif
            return (object)new EasyEDALoaderModule(client);
        }
    }
}
