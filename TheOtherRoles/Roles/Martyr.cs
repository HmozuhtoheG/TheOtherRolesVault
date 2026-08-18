using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils;
using TheOtherRoles.Modules;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
	[TORRPCHolder]
	public class Martyr : RoleBase<Martyr>
	{
		public Martyr()
		{
			RoleId = roleId = RoleId.Martyr;
		}

		public static Color color = new(0f, 0.7f, 1f);

		public static float cooldown = 30f;
		public static PlayerControl deadTarget;//当前选中的尸体玩家(按钮目标)
		private static DeadBody selectedBody;//当前亮起光圈的死体

		public static bool sacrificedFlag;//自杀后待审判标记

		// 牺牲
		public static RemoteProcess<(byte selfId, byte targetId)> Sacrifice = new("MartyrSacrifice", (message, _) =>
		{
			var sacrificer = Helpers.playerById(message.selfId);
			var target = Helpers.playerById(message.targetId);
			if (sacrificer == null || target == null) return;

			//自杀(留尸体)
			sacrificer.MurderPlayer(sacrificer, MurderResultFlags.Succeeded);
			GameHistory.overrideDeathReasonAndKiller(sacrificer, DeadPlayer.CustomDeathReason.Suicide);
			sacrificedFlag = true;

			//复活(销毁尸体+RevivePatch处理)
			foreach (var body in UnityEngine.Object.FindObjectsOfType<DeadBody>())
				if (body.ParentId == message.targetId)
				{
					UnityEngine.Object.Destroy(body.gameObject); break;
				}
			target.Revive();
			if (RoleManager.IsGhostRole(target.Data.Role.Role))
				target.RpcSetRole(RoleTypes.Crewmate);
			target.StartCoroutine(target.CoGush());

			TORGameManager.Instance?.GameStatistics.RecordEvent(new(GameStatistics.EventVariation.Revive, null, 1 << message.targetId) { RelatedTag = EventDetail.Revive });
		});

		public override void FixedUpdate()
		{
			if (player != PlayerControl.LocalPlayer || player.Data.IsDead) return;
			if (MeetingHud.Instance != null || ExileController.Instance != null) return;
			float killDist = LegacyGameOptions.KillDistances[Mathf.Clamp(GameOptionsManager.Instance.currentNormalGameOptions.KillDistance, 0, 2)];
			Vector2 truePos = player.GetTruePosition();

			DeadBody nearest = null; float best = killDist;
			foreach (var body in UnityEngine.Object.FindObjectsOfType<DeadBody>())
			{
				if (body.ParentId == player.PlayerId) continue;
				Vector2 vector = body.TruePosition - truePos;
				float magnitude = vector.magnitude;
				if (magnitude <= best && !PhysicsHelpers.AnyNonTriggersBetween(truePos, vector.normalized, magnitude, Constants.ShipAndObjectsMask))
				{
					best = magnitude; nearest = body;
				}
			}

			//目标切换
			if (nearest != selectedBody)
			{
				if (selectedBody != null)
					foreach (var sr in selectedBody.bodyRenderers) sr.material.SetFloat("_Outline", 0f);
				selectedBody = nearest;
				if (nearest != null)
					foreach (var sr in nearest.bodyRenderers)
					{
						sr.material.SetFloat("_Outline", 1f);
						sr.material.SetColor("_OutlineColor", color);
					}
			}
			deadTarget = nearest != null ? Helpers.playerById(nearest.ParentId) : null;
		}

		public static RemoteProcess<byte> Resurrection = RemotePrimitiveProcess.OfByte("MartyrResurrection", (message, _) =>
		{
			var martyr = Helpers.playerById(message);
			if (martyr == null || !martyr.Data.IsDead) return;
			martyr.Revive();
			if (AmongUsClient.Instance.AmHost && RoleManager.IsGhostRole(martyr.Data.Role.Role))
				martyr.RpcSetRole(RoleTypes.Crewmate);
			martyr.StartCoroutine(martyr.CoGush());
			if (PlayerControl.LocalPlayer == martyr) new StaticAchievementToken("martyr.judgement");
		});

		//审判
		public static void triggerJudgement(PlayerControl exiled)
		{
			if (!sacrificedFlag) return;
			if (exiled == null)//平票/跳过
			{
				sacrificedFlag = false; return;
			}
			bool revive = Helpers.isEvil(exiled);//非船员
			sacrificedFlag = false;//机会消耗
			if (revive && players.Count > 0)
			{
				Resurrection.Invoke(players[0].player.PlayerId);
			}
		}

		public static void clearAndReload()
		{
			cooldown = CustomOptionHolder.martyrCooldown.getFloat();
			deadTarget = null;
			selectedBody = null;
			players = [];
			sacrificedFlag = false;
		}
	}
}