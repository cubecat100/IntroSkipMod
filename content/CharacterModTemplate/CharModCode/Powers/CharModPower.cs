using BaseLib.Abstracts;
using BaseLib.Extensions;
using CharMod.CharModCode.Extensions;
using Godot;

namespace CharMod.CharModCode.Powers;

public abstract class CharModPower : CustomPowerModel
{
    //Loads from CharMod/images/powers/your_power.png
    public override string CustomPackedIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".PowerImagePath();
    public override string CustomBigIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".BigPowerImagePath();
}