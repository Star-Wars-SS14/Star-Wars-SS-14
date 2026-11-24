using System.Linq;
using Content.Shared._SW.Tutorial;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Server._SW.Tutorial;

public sealed partial class SWTutorialSystem
{
    private void InitializeSpecializations()
    {
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnPracticeShot);
        SubscribeLocalEvent<SWTutorialPracticeShotComponent, ProjectileHitEvent>(OnPracticeHit);
    }

    private void OnPracticeShot(Entity<GunComponent> weapon, ref GunShotEvent args)
    {
        if (!_player.TryGetSessionByEntity(args.User, out var session) ||
            !_students.TryGetValue(session.UserId, out var student) || !student.Scenario.GuidedPractice ||
            !GetTagged(student, "weapon").Contains(weapon.Owner) || !InPractice(student, weapon) ||
            !_hands.IsHolding(student.Body, weapon.Owner) || !IsWielded(student, "weapon"))
            return;

        foreach (var (uid, _) in args.Ammo)
        {
            if (uid is not { } projectile || !InPractice(student, projectile) || !HasComp<ProjectileComponent>(projectile))
                continue;
            var shot = EnsureComp<SWTutorialPracticeShotComponent>(projectile);
            shot.Student = student.Body;
            shot.Attempt = student.Attempt;
        }
    }

    private void OnPracticeHit(Entity<SWTutorialPracticeShotComponent> projectile, ref ProjectileHitEvent args)
    {
        if (!_player.TryGetSessionByEntity(projectile.Comp.Student, out var session) ||
            !_students.TryGetValue(session.UserId, out var student) || student.Attempt != projectile.Comp.Attempt ||
            args.Shooter != student.Body || !InPractice(student, args.Target) || !args.Damage.AnyPositive())
            return;
        if (CurrentStep(student).Completion is SWTutorialDefeatedCondition condition &&
            GetTagged(student, condition.Tag).Contains(args.Target))
            student.PracticeHits.Add(args.Target);
    }

    private bool IsPracticeLoaded(Student student, SWTutorialLoadedCondition condition)
    {
        foreach (var weapon in GetTagged(student, condition.Weapon))
        {
            if (!InPractice(student, weapon) || !_hands.IsHolding(student.Body, weapon) ||
                !_containers.TryGetContainer(weapon, MagazineSlot, out var slot) ||
                !GetTagged(student, condition.Magazine).Any(mag => slot.ContainedEntities.Contains(mag)))
                continue;
            var ammo = new GetAmmoCountEvent();
            RaiseLocalEvent(weapon, ref ammo);
            if (ammo.Count > 0)
                return true;
        }
        return false;
    }

    private bool IsPracticeDefeated(Robust.Shared.Player.ICommonSession session, Student student, SWTutorialDefeatedCondition condition)
    {
        var targets = GetTagged(student, condition.Tag);
        if (targets.Count == 0 || !targets.All(AllGone))
            return false;
        if (targets.All(student.PracticeHits.Contains))
            return true;
        // A melee kill or one-handed shot is not evidence of the skill being taught. Restore only the target.
        student.Tagged.Remove(condition.Tag);
        RunActions(student, condition.Retry);
        student.Note = "sw-tutorial-beat-repeat-target";
        SendState(session, student);
        return false;
    }

    private void DamagePracticePatient(Student student, SWTutorialDamagePatientAction action)
    {
        foreach (var patient in GetTagged(student, action.Tag))
        {
            if (!InPractice(student, patient))
                continue;
            _damageable.ChangeDamage(patient, action.Damage, true);
            student.PatientDamage[patient] = TotalDamage(patient);
        }
    }

    private bool IsPatientHealed(Student student, SWTutorialPatientHealedCondition condition)
    {
        var patients = GetTagged(student, condition.Tag);
        return patients.Count > 0 && patients.All(patient => InPractice(student, patient) &&
            student.PatientDamage.TryGetValue(patient, out var initial) && initial >= condition.Amount &&
            _mobState.IsAlive(patient) && TotalDamage(patient) <= initial - condition.Amount);
    }
}
