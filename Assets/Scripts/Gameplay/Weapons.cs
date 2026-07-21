using UnityEngine;

namespace TankBattle.Gameplay
{
    /// <summary>All obtainable weapons. Standard is the infinite default cannon.</summary>
    public enum WeaponType
    {
        Standard = 0,
        MachineGun = 1,
        Shotgun = 2,
        Laser = 3,
        Rocket = 4   // the "power" weapon: splash damage
    }

    /// <summary>Static description of one weapon's behaviour and looks.</summary>
    public struct WeaponDef
    {
        public string Name;        // HUD label
        public int Damage;         // per bullet/pellet
        public float Speed;        // projectile m/s
        public float FireInterval; // seconds between shots
        public float SpreadDeg;    // random cone half-angle per pellet
        public int Pellets;        // projectiles per trigger pull (shotgun > 1)
        public int Ammo;           // shots granted by a pickup; -1 = infinite
        public float SplashRadius; // 0 = direct hit only (rocket > 0)
        public Color BulletColor;  // tint for bullet + trail
        public float BulletScale;  // visual size of the projectile
    }

    /// <summary>
    /// The weapon table. Index-aligned with WeaponType. Server uses damage and
    /// timing values; clients use color/scale for visuals only.
    /// </summary>
    public static class Weapons
    {
        public static readonly WeaponDef[] Defs =
        {
            new WeaponDef { Name = "CANNON",  Damage = 25, Speed = 22f, FireInterval = 0.50f,
                SpreadDeg = 0f,  Pellets = 1, Ammo = -1, SplashRadius = 0f,
                BulletColor = new Color(1f, 0.85f, 0.2f), BulletScale = 0.35f },

            new WeaponDef { Name = "MACHINE GUN", Damage = 10, Speed = 30f, FireInterval = 0.14f,
                SpreadDeg = 2.0f, Pellets = 1, Ammo = 50, SplashRadius = 0f,
                BulletColor = new Color(1f, 1f, 0.6f), BulletScale = 0.20f },

            new WeaponDef { Name = "SHOTGUN", Damage = 14, Speed = 20f, FireInterval = 0.90f,
                SpreadDeg = 8.0f, Pellets = 5, Ammo = 12, SplashRadius = 0f,
                BulletColor = new Color(1f, 0.55f, 0.15f), BulletScale = 0.22f },

            new WeaponDef { Name = "LASER", Damage = 40, Speed = 48f, FireInterval = 0.70f,
                SpreadDeg = 0f,  Pellets = 1, Ammo = 10, SplashRadius = 0f,
                BulletColor = new Color(0.25f, 0.95f, 0.95f), BulletScale = 0.25f },

            new WeaponDef { Name = "ROCKET", Damage = 60, Speed = 16f, FireInterval = 1.20f,
                SpreadDeg = 0f,  Pellets = 1, Ammo = 5, SplashRadius = 4.5f,
                BulletColor = new Color(1f, 0.35f, 0.10f), BulletScale = 0.50f },
        };

        /// <summary>Weapon order used by Gun Game (tier 0 -> last).</summary>
        public static readonly WeaponType[] GunGameOrder =
        {
            WeaponType.Standard, WeaponType.MachineGun, WeaponType.Shotgun,
            WeaponType.Laser, WeaponType.Rocket
        };

        public static WeaponDef Get(int index)
        {
            if (index < 0 || index >= Defs.Length) index = 0;
            return Defs[index];
        }

        /// <summary>Random pickup type - never Standard, rockets a bit rarer.</summary>
        public static WeaponType RandomPickup()
        {
            // MachineGun / Shotgun / Laser twice each, Rocket once -> 1-in-7 rocket.
            WeaponType[] pool =
            {
                WeaponType.MachineGun, WeaponType.MachineGun,
                WeaponType.Shotgun, WeaponType.Shotgun,
                WeaponType.Laser, WeaponType.Laser,
                WeaponType.Rocket
            };
            return pool[Random.Range(0, pool.Length)];
        }
    }
}
