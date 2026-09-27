using System.Collections.Generic;

namespace cozyhouse;

public class PcSkinType
{
	public string SkinTypeName { get; private set; }

	public List<PcSkinInfo> PcSkinInfos { get; private set; } = new List<PcSkinInfo>();

	public PcSkinType(string skinTypeName, List<PcSkinInfo> pcSkinInfoFields)
	{
		SkinTypeName = skinTypeName;
		PcSkinInfos = pcSkinInfoFields;
	}
}
