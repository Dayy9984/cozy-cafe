using System.Collections.Generic;
using TS;

namespace cozyhouse;

public class PcSkinColorGroup : SelectableGroup<PcSkinInfo>
{
	public override List<SelectableSlot<PcSkinInfo>> Initialize(List<PcSkinInfo> datas)
	{
		return base.Initialize(datas);
	}
}
