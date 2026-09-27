using System;
using cozyhouse.dlc;

namespace cozyhouse;

[Serializable]
public class DecoSkinInfoFields : IKey
{
	public int DecoSkinKey;

	public int RoomKey;

	public string DecorableType;

	public int DecoGroupKey;

	public int Cost;

	public bool IsDefaultUnlocked;

	public bool IsCreateAnimation;

	public bool Interactable;

	public string IconPath;

	public string ImageTopPath;

	public string ImageBottomPath;

	public EDLCType DLCType;

	public bool isDemoAvailable;

	public int Key => DecoSkinKey;
}
