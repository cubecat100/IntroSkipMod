using BaseLib.Abstracts;
using BaseLib.Extensions;
using ContentMod.ContentModCode.Extensions;
using Godot;

namespace ContentMod.ContentModCode.Relics;

public abstract class ContentModRelic : CustomRelicModel
{
    //ContentMod/images/relics
    public override string PackedIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".RelicImagePath();
    protected override string PackedIconOutlinePath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}_outline.png".RelicImagePath();
    protected override string BigIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".BigRelicImagePath();
}