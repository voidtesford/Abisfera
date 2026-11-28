// ReSharper disable once CheckNamespace
namespace Content.Client.Stylesheets;

public abstract partial class BaseStylesheet
{
    /// <summary>Exposed config object (StyleTheme or NoConfig).</summary>
    protected object Config => _config;
}
