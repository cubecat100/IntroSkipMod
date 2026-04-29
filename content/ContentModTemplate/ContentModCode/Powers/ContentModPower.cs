using BaseLib.Abstracts;
using BaseLib.Extensions;
using ContentMod.ContentModCode.Extensions;
using Godot;

namespace ContentMod.ContentModCode.Powers;

public abstract class ContentModPower : CustomPowerModel
{
    //Loads from ContentMod/images/powers/your_power.png
    public override string CustomPackedIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".PowerImagePath();
    public override string CustomBigIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".BigPowerImagePath();
}