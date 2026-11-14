using System;
using System.Collections.Generic;
using Arkon.Core;

public static class LeaseCases
{
    public static void Run(Action<bool,string> check)
    {
        var pool = new LeasePool<ProjectileModel>(() => new ProjectileModel(), 1, 1);
        var old = pool.Acquire(); PoolLease<ProjectileModel> fresh = default;
        old.Item.Setup(Vec2.Zero, Vec2.Zero, 650, _ => {
            // Same sequence as scene disable -> enable -> spawn during impact.
            pool.ReleaseAll(); fresh = pool.Acquire();
            fresh.Item.Setup(new Vec2(100,0), new Vec2(200,0),650,null);
        },recycle:_=>pool.Release(old));
        old.Item.Tick(.1f,null);
        check(ReferenceEquals(old.Item,fresh.Item),"Reentrant scene restart reuses the warmed object.");
        check(pool.IsCurrent(fresh) && fresh.Item.Active && pool.ActiveCount==1,
            "An old impact recycle cannot retire the new projectile lease.");
        var snapshot = new List<PoolLease<ProjectileModel>>(); pool.Snapshot(snapshot);
        pool.ReleaseAll(); var replacement=pool.Acquire();
        replacement.Item.Setup(new Vec2(300,0),new Vec2(400,0),650,null);
        foreach(var lease in snapshot) if(pool.IsCurrent(lease)) lease.Item.Tick(.1f,null);
        check(replacement.Item.Position.Equals(new Vec2(300,0)),"A frame snapshot does not tick a later reused lease.");
        check(!pool.Release(fresh) && pool.Release(replacement),"Only the live lease can return an object.");
    }
}
