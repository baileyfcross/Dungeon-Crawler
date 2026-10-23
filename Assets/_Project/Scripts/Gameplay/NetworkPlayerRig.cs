using System;
using SpaceCrawler.Core;
using UnityEngine;
using CoreVector = System.Numerics.Vector2;

namespace SpaceCrawler.Gameplay
{
    /// <summary>Reuses existing physics/combat on the host; client rig only presents replicated state.</summary>
    public sealed class NetworkPlayerRig : MonoBehaviour
    {
        [SerializeField] private PlayerMotor motor;
        [SerializeField] private PlayerInputAdapter input;
        [SerializeField] private PlayerPresentation presentation;
        [SerializeField] private CombatResolver combat;
        [SerializeField] private SpriteRenderer bodySprite;
        [SerializeField] private Collider2D[] playerColliders;
        private PlayerState replica;
        private WeaponConfiguration weapon;
        private bool authoritative;
        private long presentedAttacks;
        private Color aliveColor;
        private bool receivedSnapshot;
        private readonly PlayerSoundState soundState=new PlayerSoundState();
        public PlayerVitals Vitals { get; private set; } = PlayerVitals.Starting;
        public float ReviveProgress { get; set; }
        public PlayerSimulation Simulation { get; private set; }
        public PlayerMotor Motor => motor;
        public PlayerInputAdapter Input => input;
        public CombatResolver Combat => combat;
        public Collider2D[] Colliders => playerColliders;
        public PlayerState State => authoritative ? Simulation.State : replica;
        public PlayerStateSnapshot LastSnapshot { get; private set; }

        public void Bind(PlayerIdentity identity, bool hostAuthority, bool local, WeaponConfiguration configuration)
        {
            authoritative = hostAuthority; weapon = configuration;
            replica = new PlayerState(identity, CoreVector.Zero, CoreVector.Zero, CoreVector.UnitX);
            bodySprite.color = local ? new Color(0.3f, 0.86f, 0.83f) : new Color(0.87f, 0.56f, 0.95f);
            aliveColor = bodySprite.color;
            if (authoritative)
            {
                Simulation = new PlayerSimulation(identity, 5);
                motor.Bind(Simulation);
                combat.Bind(Simulation, weapon);
            }
            else
            {
                motor.enabled = false; motor.Body.simulated = false; combat.enabled = false;
                foreach (var collider in playerColliders) collider.enabled = false;
            }
        }

        public PlayerStateSnapshot Capture(bool inputEnabled)
        {
            motor.CaptureState();
            var state = Simulation.State;
            var last = combat.Simulation.LastExecution;
            return new PlayerStateSnapshot {
                Player = state.Player.Value,
                Position = new Vector2(state.Position.X, state.Position.Y), Velocity = new Vector2(state.Velocity.X, state.Velocity.Y),
                Aim = new Vector2(state.AimDirection.X, state.AimDirection.Y),
                Attacks = combat.Simulation.AcceptedAttackCount, Cooldown = combat.Simulation.CooldownRemaining, InputEnabled = inputEnabled,
                Health = Vitals.Current, MaximumHealth = Vitals.Maximum, LifeState = (int)Vitals.State, ReviveProgress = ReviveProgress,
                AttackOrigin = last == null ? Vector2.zero : new Vector2(last.Origin.X, last.Origin.Y),
                AttackAim = last == null ? Vector2.right : new Vector2(last.AimDirection.X, last.AimDirection.Y)
            };
        }

        public void Apply(PlayerStateSnapshot state)
        {
            if (authoritative || state.Player != replica.Player.Value) throw new InvalidOperationException("Invalid replica ownership.");
            LastSnapshot = state;
            PresentVitals(new PlayerVitals(state.Health, state.MaximumHealth, (PlayerLifeState)state.LifeState));
            if (float.IsNaN(state.ReviveProgress) || float.IsInfinity(state.ReviveProgress) || state.ReviveProgress < 0 || state.ReviveProgress > RunLife.ReviveSeconds)
                throw new InvalidOperationException("Invalid revival progress.");
            ReviveProgress = state.ReviveProgress;
            replica = new PlayerState(replica.Player, new CoreVector(state.Position.x, state.Position.y),
                new CoreVector(state.Velocity.x, state.Velocity.y), new CoreVector(state.Aim.x, state.Aim.y));
            motor.transform.position = new Vector3(state.Position.x, state.Position.y, 0);
            if (state.Attacks > presentedAttacks)
                combat.Presentation.Present(weapon, state.AttackOrigin, state.AttackAim,receivedSnapshot);
            presentedAttacks = state.Attacks;
            receivedSnapshot=true;
        }

        public void ClearRoom()
        {
            combat.ClearRoom();
            if (authoritative) motor.Stop();
        }
        public void Present() => presentation.Present(State);
        public void PresentVitals(PlayerVitals vitals)
        {
            soundState.Observe(vitals);
            Vitals = vitals; bodySprite.color = vitals.State == PlayerLifeState.Alive ? aliveColor : new Color(.45f, .45f, .5f);
            if (Simulation == null) return;
            Simulation.SetLifeActive(vitals.State == PlayerLifeState.Alive);
            if (!Simulation.CanAct) motor.Stop();
        }
    }
}
