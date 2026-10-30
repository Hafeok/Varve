// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Varve.WebUi.Pages.Services;

namespace Varve.WebUi.SignIn;

/// <summary>
/// The sign-in in flight in the tab's session storage, under one key, through
/// <c>js/webui.js</c>. Taken out as it is read (ADR 0201).
/// </summary>
internal sealed class SessionSignInStore(IJSRuntime js) : ISignInStore
{
    private const string Key = "varve.signin";

    public async Task PutAsync(PendingSignIn pending) =>
        await js.InvokeVoidAsync("varveWebUi.sessionPut", Key, pending.ToJson()).ConfigureAwait(false);

    public async Task<PendingSignIn?> TakeAsync() =>
        PendingSignIn.FromJson(await js.InvokeAsync<string?>("varveWebUi.sessionTake", Key).ConfigureAwait(false));
}

/// <summary>A top-level navigation, which leaves the application and its heap.</summary>
internal sealed class NavigationBrowser(NavigationManager navigation) : IBrowser
{
    public void Leave(Uri address) => navigation.NavigateTo(address.AbsoluteUri, forceLoad: true);
}
