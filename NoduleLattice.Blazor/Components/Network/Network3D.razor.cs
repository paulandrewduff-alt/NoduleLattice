using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace NoduleLattice.Blazor.Components.Network;

public partial class Network3D : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private IJSObjectReference? _module;
    private IJSObjectReference? _instance;

    private long _lastStep = -1;
    private string? _initError;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Never attempt JS if we've already failed init.
        if (_initError is not null) return;

        if (firstRender)
        {
            try
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", "/js/network3d.js");
                _instance = await _module.InvokeAsync<IJSObjectReference>("createNetwork3D", _host);
            }
            catch (JSException ex)
            {
                // This is your [object Event] case. Capture and stop future attempts.
                _initError = $"JS init failed: {ex.Message}";
                Console.Error.WriteLine($"[Network3D] {_initError}");
                return;
            }
            catch (Exception ex)
            {
                _initError = $"JS init failed: {ex.GetType().Name}: {ex.Message}";
                Console.Error.WriteLine($"[Network3D] {_initError}\n{ex}");
                return;
            }
        }

        if (_module is null || _instance is null) return;
        if (Snapshot is null) return;
        if (Snapshot.StepIndex == _lastStep) return;

        _lastStep = Snapshot.StepIndex;

        try
        {
            await _module.InvokeVoidAsync("updateNetwork3D", _instance, Snapshot);
        }
        catch (JSDisconnectedException)
        {
            // circuit gone; ignore
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[Network3D] JS update failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Network3D] Update failed: {ex}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null && _instance is not null)
                await _module.InvokeVoidAsync("disposeNetwork3D", _instance);
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch { }

        try
        {
            if (_instance is not null)
                await _instance.DisposeAsync();
        }
        catch { }

        try
        {
            if (_module is not null)
                await _module.DisposeAsync();
        }
        catch { }
    }
}
