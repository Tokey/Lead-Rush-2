using System;
using UnityEngine;

namespace LR2.Characters
{
    public enum SoldierStance { Standing, Crouching, Prone }

    public enum SoldierMoveState { Idle, Walking, Sprinting, InAir, Sliding, Mantling }

    /// <summary>
    /// One frame of "controller" input. Humans fill it from devices, bots from their brain. Because both drive the
    /// exact same SoldierController, bots move, aim, recoil and reload under the same rules as the player.
    /// </summary>
    [Serializable]
    public struct SoldierInput
    {
        public Vector2 move;
        public Vector2 look;
        public bool absoluteLook;
        public float aimYaw;
        public float aimPitch;
        public bool fire;
        public bool aim;
        public bool sprint;
        public bool reload;
        public bool jump;
        public bool crouch;
        public bool prone;
        public float lean;
        public int weaponSlot;
        public bool nextWeapon;
        public bool interact;

        public static SoldierInput Empty => new SoldierInput { weaponSlot = -1 };

        public void ResetOneShots()
        {
            reload = false;
            jump = false;
            crouch = false;
            prone = false;
            nextWeapon = false;
            interact = false;
            weaponSlot = -1;
        }

        public void ClearAll()
        {
            this = Empty;
        }
    }

    public interface ISoldierInputSource
    {
        void GetInput(SoldierController soldier, ref SoldierInput input);
    }
}
