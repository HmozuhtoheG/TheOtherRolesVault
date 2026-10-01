using TheOtherRoles.MetaContext;
using UnityEngine;

namespace TheOtherRoles.Modules.Emotes
{
    public abstract class EmoteDefinition
    {
        public abstract string Id { get; }

        public abstract Sprite Icon { get; }

        public virtual Sprite[] Frames => null;

        public virtual float FrameSeconds => 0.1f;

        public virtual float[] FrameDelays => null;

        public virtual byte[] Payload => null;

        public abstract void Play(PlayerControl player);
    }

    public abstract class BubbleEmote : EmoteDefinition
    {
        public override void Play(PlayerControl player)
        {
            EmoteWheel.ShowBubble(player, this);
        }
    }

    public class IconEmote : BubbleEmote
    {
        private readonly Sprite sprite;

        public int Index { get; }

        public IconEmote(int index, Sprite sprite)
        {
            Index = index;
            this.sprite = sprite;
        }

        public override string Id => "i" + Index;

        public override Sprite Icon => sprite;

        public override Sprite[] Frames => new[] { sprite };
    }

    public class HandEmote : EmoteDefinition
    {
        public enum HandKind
        {
            Good,
            Bad,
            Wave
        }

        private readonly HandKind kind;

        public HandEmote(HandKind kind)
        {
            this.kind = kind;
        }

        public override string Id => kind switch
        {
            HandKind.Good => "nb.good",
            HandKind.Bad => "nb.bad",
            _ => "nb.wave"
        };

        public override Sprite Icon => kind switch
        {
            HandKind.Good => EmoteHand.GetHandSprite(1),
            HandKind.Bad => EmoteHand.GetHandSprite(3),
            _ => EmoteHand.GetHandSprite(2)
        };

        public override void Play(PlayerControl player)
        {
            EmoteHand.Play(kind, player);
        }
    }

    public class DanceEmote : EmoteDefinition
    {
        private static Sprite icon;

        public override string Id => "nb.dance";

        public override Sprite Icon
        {
            get
            {
                if (icon == null) icon = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.DanceEmoteIcon.png", 100f);
                return icon;
            }
        }

        public override void Play(PlayerControl player)
        {
            EmoteDance.Play(player);
        }
    }
}
