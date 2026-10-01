using System;
using System.Collections.Generic;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.Modules.Emotes
{
    [TORRPCHolder]
    public static class EmoteNet
    {
        private const int ChunkSize = 400;
        private const int ChunksPerFrame = 3;
        private const float TransferTimeout = 5f;

        private static readonly HashSet<string> broadcasted = new();
        private static readonly Queue<Outgoing> outgoing = new();
        private static readonly Dictionary<string, Incoming> incoming = new();

        private class Outgoing
        {
            public string Id;
            public byte[] Payload;
            public int Offset;
            public int Total;
            public int Index;
        }

        private class Incoming
        {
            public byte[][] Parts;
            public int Received;
            public float Time;
        }

        public static RemoteProcess<(byte senderId, string emoteId, int index, int total, byte[] chunk)> ShowEmote =
            new("EmoteShow", (message, _) =>
            {
                var sender = Helpers.playerById(message.senderId);
                if (sender == null || message.emoteId == null) return;

                if (message.total <= 1)
                {
                    Play(sender, message.emoteId, message.chunk != null && message.chunk.Length > 0 ? message.chunk : null);
                    return;
                }

                if (message.total > 1024 || message.index >= message.total || message.chunk == null) return;

                if (!incoming.TryGetValue(message.emoteId, out var transfer))
                {
                    transfer = new Incoming { Parts = new byte[message.total][] };
                    incoming[message.emoteId] = transfer;
                }
                if (transfer.Parts.Length != message.total) return;

                transfer.Time = Time.unscaledTime;
                if (transfer.Parts[message.index] == null)
                {
                    transfer.Parts[message.index] = message.chunk;
                    transfer.Received++;
                }

                if (transfer.Received < message.total) return;

                incoming.Remove(message.emoteId);

                int length = 0;
                foreach (var part in transfer.Parts) length += part.Length;

                var payload = new byte[length];
                int offset = 0;
                foreach (var part in transfer.Parts)
                {
                    Buffer.BlockCopy(part, 0, payload, offset, part.Length);
                    offset += part.Length;
                }

                Play(sender, message.emoteId, payload);
            });

        private static void Play(PlayerControl sender, string emoteId, byte[] payload)
        {
            var emote = EmoteCatalog.Get(emoteId);
            if (emote == null && payload != null && payload.Length > 0)
            {
                emote = EmoteCatalog.RegisterImported(emoteId, payload);
            }

            emote?.Play(sender);
        }

        public static void Send(EmoteDefinition emote)
        {
            if (emote == null || PlayerControl.LocalPlayer == null) return;

            var data = emote.Payload;
            if (data == null || data.Length == 0 || !broadcasted.Add(emote.Id))
            {
                ShowEmote.Invoke((PlayerControl.LocalPlayer.PlayerId, emote.Id, 0, 1, Array.Empty<byte>()));
                return;
            }

            int total = (data.Length + ChunkSize - 1) / ChunkSize;
            outgoing.Enqueue(new Outgoing { Id = emote.Id, Payload = data, Total = total });
            TheOtherRolesPlugin.Logger.LogInfo($"[EmoteNet] streaming {emote.Id}: {data.Length} bytes in {total} chunk(s)");
        }

        public static void Tick()
        {
            for (int i = 0; i < ChunksPerFrame && outgoing.Count > 0; i++)
            {
                var transfer = outgoing.Peek();
                int length = Mathf.Min(ChunkSize, transfer.Payload.Length - transfer.Offset);

                var chunk = new byte[length];
                Buffer.BlockCopy(transfer.Payload, transfer.Offset, chunk, 0, length);

                ShowEmote.Invoke((PlayerControl.LocalPlayer.PlayerId, transfer.Id, transfer.Index, transfer.Total, chunk));

                transfer.Offset += length;
                transfer.Index++;
                if (transfer.Index >= transfer.Total) outgoing.Dequeue();
            }

            if (incoming.Count == 0) return;

            float now = Time.unscaledTime;
            List<string> stale = null;
            foreach (var pair in incoming)
            {
                if (now - pair.Value.Time > TransferTimeout) (stale ??= new List<string>()).Add(pair.Key);
            }
            if (stale == null) return;

            foreach (var key in stale)
            {
                var parts = incoming[key].Parts;
                int received = incoming[key].Received;
                incoming.Remove(key);
                TheOtherRolesPlugin.Logger.LogWarning($"[EmoteNet] dropped incomplete transfer {key} ({received}/{parts.Length})");
            }
        }

        public static void ResetBroadcast()
        {
            broadcasted.Clear();
            outgoing.Clear();
            incoming.Clear();
        }
    }
}
