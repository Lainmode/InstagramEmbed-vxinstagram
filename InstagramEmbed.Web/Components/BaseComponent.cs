using InstagramEmbed.DataAccess;
using InstagramEmbed.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using System.Security.Claims;

[StreamRendering(true)]
public abstract class BaseComponent : ComponentBase
{
    [Inject] protected NavigationManager NavigationManager { get; set; } = null!;
    [Inject] protected IHttpContextAccessor HttpContextAccessor { get; set; } = null!;
    [Inject] protected IAuthenticationService AuthenticationService { get; set; } = null!;
    [Inject] protected AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;
    [Inject] protected IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] protected InstagramContext Db { get; set; } = null!;


    public User? CurrentUser { get; set; } = null!;

    protected bool IsLoading { get; set; } = true;



    protected override async Task OnInitializedAsync()
    {
        try
        {
            await SetCurrentUserAsync();
        }
        catch (Exception e) { }

        if (CurrentUser == null)
        {
            NavigationManager.NavigateTo("/login");
            return;
        }
        else
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    private async Task SetCurrentUserAsync()
    {
        var context = HttpContextAccessor.HttpContext;
        if (context == null)
            return;


        var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user?.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = user.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out int userId))
            {
                CurrentUser = Db.Users.Find(userId);
            }
        }
    }
}

