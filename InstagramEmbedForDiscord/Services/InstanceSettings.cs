namespace InstagramEmbed.Application.Services;

public sealed class InstanceSettings
{
    public string Name { get; }
    public string BaseUrl { get; }
    public bool ShowDetails { get; }

    public InstanceSettings(IConfiguration config)
    {
        Name = config["Instance:Name"] ?? "Instagram embeds";
        var value = config["Instance:PublicUrl"] ?? "http://localhost:8080";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/"
            || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Instance:PublicUrl must be an HTTP(S) origin.");
        BaseUrl = uri.GetLeftPart(UriPartial.Authority);
        ShowDetails = config.GetValue("Instance:ShowDetails", true);
    }
}
