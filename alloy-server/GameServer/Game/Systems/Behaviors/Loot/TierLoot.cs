using Common;
using Common.Resources.Xml.Descriptors;
using GameServer.Game.Systems.Combat;
using GameServer.Game.Entities;

namespace GameServer.Game.Systems.Behaviors.Loot;

public class TierLoot : ILoot {

    public TierLoot(int tier, ItemType itemType, float threshold, float chance) {
        
    }
    
    public void Populate(ref EntityView host, ref Queue<Item> drops, ref DamageRecord record) {
        throw new NotImplementedException();
    }
}