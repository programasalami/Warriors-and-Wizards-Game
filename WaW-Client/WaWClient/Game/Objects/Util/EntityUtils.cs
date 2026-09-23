using System.Collections.Generic;
using WaWClient.Game;
using OpenTK.Mathematics;

namespace WaWClient.Game.Objects.Util;

public static class EntityUtils {

    public static bool FindClosestInteractableInRadius(Vector2 position, float radius, out Entity interactable) {
        interactable = null;

        var closest = float.MaxValue;

        foreach (var (_, entity) in Map.InteractiveObjects) {
            if (entity == Map.LocalPlayer) {
                continue;
            }
            
            Vector2.DistanceSquared(in position, in entity.Position, out var distance);
            
            if (distance <= radius && distance < closest) {
                closest = distance;
                interactable = entity;  
            }
        }

        return interactable != null;
    }
    
    public static Entity FindClosestEntityInRadius(Entity player, IEnumerable<Entity> entities, float radius) {
        Entity closestEntity = null;
        var closestDistance = float.MaxValue;

        foreach (var entity in entities) {
            Vector2.DistanceSquared(player.Position, entity.Position, out var distance);
            
            if (entity == Map.LocalPlayer)
                continue;
            
            if (distance <= radius && distance < closestDistance) {
                closestDistance = distance;
                closestEntity = entity;  
            }
        }
        
        return closestEntity;
    }
    
    public static Entity FindClosestSpecialInRadius(Entity player, IEnumerable<Entity> entities, float radius) {
        Entity closestEntity = null;
        var closestDistance = float.MaxValue;

        foreach (var entity in entities) {
            Vector2.DistanceSquared(player.Position, entity.Position, out var distance);
            
            if (IsCharacter(entity))
                continue;
            
            if (distance <= radius && distance < closestDistance) {
                closestDistance = distance;
                closestEntity = entity;  
            }
        }
        
        return closestEntity;
    }
    
    // distanceSqr is a SQUARED distance (what Vector2.DistanceSquared gives), radius is in tiles. Comparing the squared distance straight against the radius (as this
    // used to) made a "0.5 tile" hit really reach 0.71 tiles, while the server only accepts 0.5 (HitValidation.HitRadius).
    public static bool IsWithinHitRadius(float distanceSqr, float radius) => distanceSqr <= radius * radius;

    public static Entity GetClosestPlayer(Vector2 position, float radius) {
        var entities = Map.Entities.Values;
        PerfCounters.EntityScansThisFrame += entities.Count;
        Entity en = null;
        var enDist = float.MaxValue;

        foreach (var entity in entities) {
            if (entity is not Player)
                continue;
            
            Vector2.DistanceSquared(position, entity.Position, out var dist);
            
            if (!IsWithinHitRadius(dist, radius) || dist >= enDist)
                continue;
            en = entity;
            enDist = dist;
        }

        return en;
    }
    
    public static Entity GetClosestEnemy(Vector2 position, float radius) {
        var entities = Map.Enemies.Values;
        PerfCounters.EntityScansThisFrame += entities.Count;
        Entity en = null;
        var enDist = float.MaxValue;

        foreach (var entity in entities) {
            Vector2.DistanceSquared(position, entity.Position, out var dist);
            if (!IsWithinHitRadius(dist, radius) || dist >= enDist)
                continue;
            en = entity;
            enDist = dist;
        }

        return en;
    }

    public static bool IsCharacter(Entity entity) => entity.Properties.IsEnemy || entity.Properties.IsAlly || entity.Properties.IsPlayer || entity.Properties.Class == "Character";
}