using System;
using SpaceCrawler.Core;
using UnityEngine;

namespace SpaceCrawler.Gameplay
{
    /// <summary>A physical request adapter; no graph mutation, prefab loading, or destination selection.</summary>
    public sealed class RoomExitTrigger : MonoBehaviour
    {
        [SerializeField] private BoxCollider2D trigger;
        private Rigidbody2D controlledBody;
        private readonly System.Collections.Generic.HashSet<Rigidbody2D> blockedArrivals = new System.Collections.Generic.HashSet<Rigidbody2D>();
        public void SuppressUntilDeparture(Rigidbody2D body) => blockedArrivals.Add(body);
        private Func<ExitIntent, bool> request;
        private ExitIntent intent;
        private Action<RoomConnection, Collider2D, bool> presence;
        private RoomConnection connection;
        public BoxCollider2D Trigger => trigger;
        public RunRoomId Destination => intent.Destination;
        public RunRoomId Source => intent.CurrentRoom;
        public bool IsConfigured => request != null;

        public void Configure(PlayerIdentity player, Rigidbody2D body, RoomConnection connection, Func<ExitIntent, bool> submit)
        {
            if (body == null || submit == null) throw new ArgumentException("An explicit player body and request receiver are required.");
            controlledBody = body;
            presence = null;
            intent = new ExitIntent(player, connection.From, connection.To);
            request = submit;
            gameObject.SetActive(true);
        }

        public void DisableExit()
        {
            blockedArrivals.Clear();
            presence = null;
            request = null;
            controlledBody = null;
            gameObject.SetActive(false);
        }

        public bool TryEnter(Collider2D other)
        {
            if (other != null && other.attachedRigidbody != null && blockedArrivals.Contains(other.attachedRigidbody)) return false;
            if (isActiveAndEnabled && other != null && presence != null)
            { presence(connection, other, true); return true; }
            return isActiveAndEnabled && trigger != null && trigger.enabled && request != null
                && other != null && other.attachedRigidbody == controlledBody && request(intent);
        }

        private void OnTriggerEnter2D(Collider2D other) => TryEnter(other);
        private void OnTriggerStay2D(Collider2D other) => TryEnter(other);
        private void OnTriggerExit2D(Collider2D other)
        { if (other.attachedRigidbody != null) blockedArrivals.Remove(other.attachedRigidbody); presence?.Invoke(connection,other,false); }

        public void ConfigureReadiness(RoomConnection edge, Action<RoomConnection, Collider2D, bool> receiver)
        {
            request = null; controlledBody = null; connection = edge; presence = receiver;
            intent = new ExitIntent(default, edge.From, edge.To);
            gameObject.SetActive(true);
        }
    }
}
