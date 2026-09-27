using System;
using UnityEngine;

namespace cozyhouse.scriptableobjects;

[Serializable]
public class SkinColorData
{
	[SerializeField]
	public EColorType colorType;

	[SerializeField]
	public Sprite iconColor;
}
