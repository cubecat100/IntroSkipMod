using BaseLib.Abstracts;
using BaseLib.Utils;
using CharMod.CharModCode.Character;

namespace CharMod.CharModCode.Potions;

[Pool(typeof(CharModPotionPool))]
public abstract class CharModPotion : CustomPotionModel;