using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using CharMod.CharModCode.Character;
using CharMod.CharModCode.Extensions;
using Godot;

namespace CharMod.CharModCode.Relics;

[Pool(typeof(CharModRelicPool))]
public abstract class CharModRelic : CustomRelicModel
{
    public override string PackedIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".RelicImagePath();
    protected override string PackedIconOutlinePath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}_outline.png".RelicImagePath();
    protected override string BigIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".BigRelicImagePath();
}