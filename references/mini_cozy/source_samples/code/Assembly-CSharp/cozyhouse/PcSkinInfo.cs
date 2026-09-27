using System;
using System.Collections.Generic;
using cozyhouse.dlc;

namespace cozyhouse;

[Serializable]
public class PcSkinInfo : IKey
{
	public int PcSkinKey;

	public string PartsCategory;

	public string DecorableType;

	public string GroupKeys;

	public EColorType ColorType;

	public int Cost;

	public bool HasUpperLayer;

	public bool IsDefaultUnlocked;

	public string IconPath;

	public EDLCType DLCType;

	public bool IsDemoAvailable;

	public int Key => PcSkinKey;

	public List<int> GetGroupKeys()
	{
		List<int> list = new List<int>();
		string[] array = GroupKeys.Split(',', ' ');
		foreach (string text in array)
		{
			if (!string.IsNullOrEmpty(text))
			{
				list.Add(int.Parse(text));
			}
		}
		return list;
	}
}
