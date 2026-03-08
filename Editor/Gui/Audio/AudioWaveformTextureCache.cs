#nullable enable
using SharpDX.Direct3D11;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.Resource;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Editor.Gui.Audio;

internal static class AudioWaveformTextureCache
{
    internal static bool TryGetShaderResourceView(AudioClipResourceHandle clipHandle, out ShaderResourceView? srv)
    {
        srv = null;
        if (!AudioImageFactory.TryGetOrCreateImagePathForClip(clipHandle, out var imagePath) || string.IsNullOrEmpty(imagePath))
            return false;

        if (_srvByImagePath.TryGetValue(imagePath, out var cachedSrv) && cachedSrv is { IsDisposed: false })
        {
            srv = cachedSrv;
            return true;
        }

        var textureResource = ResourceManager.CreateTextureResource(imagePath, clipHandle.Owner);
        if (textureResource.Value == null)
            return false;

        var newSrv = default(ShaderResourceView);
        textureResource.Value.CreateShaderResourceView(ref newSrv, imagePath);
        if (newSrv is null || newSrv.IsDisposed)
            return false;

        _textureResourceByImagePath[imagePath] = textureResource;
        _srvByImagePath[imagePath] = newSrv;
        srv = newSrv;
        return true;
    }

    private static readonly Dictionary<string, Resource<Texture2D>> _textureResourceByImagePath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ShaderResourceView> _srvByImagePath = new(StringComparer.OrdinalIgnoreCase);
}
