﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public class BeamHandler : MonoBehaviour
{
    public const int MAX_BEAMS = 1000;
    public static int beamCount = 0;

    // FIXME: we should track start and endpoints, do this with delta-t
    const float SPEED = 5;

    private const float EPS = 1e-4f;

    public GameHandler game;

    new public LineRenderer renderer;
    new public CapsuleCollider collider;

    public float start;
    public float end;

    // Decremented on every beam split, to avoid accidentally going infinite.
    public int intensity;

    public bool powered;

    public TileHandler endPoint;
    public Vector3 hitPoint;
    public Vector3 hitNormal;
    public List<BeamHandler> children;

    public SprayEffectHandler sprayEffect;

    int layerMask;

    public void InitBeam(GameHandler h, Vector3 start, Vector3 dir, BeamHandler template)
    {
        if (beamCount >= MAX_BEAMS) {
            GameObject.Destroy(gameObject);
            return;
        }
        this.intensity = (template != null) ? template.intensity : 10;

        beamCount++;

        this.game = h;

        transform.rotation = Quaternion.FromToRotation(Vector3.right, dir);
        transform.position = start;

        this.start = 0;
        this.end = 0;


        this.powered = true;

        this.endPoint = null;
        this.children = null;

        this.sprayEffect = game.CreateSprayEffect();

        layerMask =
            1 << LayerMask.NameToLayer("Tile") |
            1 << LayerMask.NameToLayer("Wall");

        SetEndpoints();
    }

    // Start is called before the first frame update
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
    }

    // Use custom update called by GameHandler
    public void Process(float dt)
    {
        // For now assume that dt > 0
        float time = game.SimTime();

        float dl = dt * SPEED;
        RaycastHit hit;
        bool res = Physics.Raycast(
                    GetPoint(start),
                    GetDir(),
                    out hit,
                    end - start + dl,
                    layerMask);

        HandleCollision(dt, start, end, res, hit);
        if (res) {
            end = start + hit.distance;
        } else {
            end += dl;
        }
        if (!powered) {
            start += dl;
        }

        if (start >= end) {
            DepowerChildren();
            GameObject.Destroy(sprayEffect);
            GameObject.Destroy(gameObject);
            beamCount--;
            return;
        }

        SetEndpoints();
    }

    private void HandleCollision(float dt, float start, float end, bool hasHit, RaycastHit hit)
    {
        TileHandler tile = null;
        if (hasHit) {
            tile = hit
                .collider
                .gameObject
                .GetComponentInParent<TileHandler>();
        }

        bool hitMatches = hasHit && endPoint == tile &&
                ApproxEqual(hit.point, hitPoint) &&
                ApproxEqual(hit.normal, hitNormal);
        if (children != null && (!hasHit || !hitMatches)) {
            // If we have a real hit that doesn't exist anymore (because the
            // source moved), stop powering the children & stop any spray effects
            DepowerChildren();
            sprayEffect.Stop();
            endPoint = null;
            children = null;
        }

        if (!hasHit) {
            sprayEffect.Stop();
            return;
        }

        if (hitMatches) {
            // We've already handled this collision
            return;
        }

        // If we didn't hit this just in the new segment of the ray
        if (hit.distance < end - start) {
            float newStart = start + hit.distance + FindOtherSide(hit);
            if (newStart <= end - 0.01) {
                // New ray!
                var dir = GetDir();
                var beam = game.CreateBeam(transform.position + newStart * dir, dir, this);

                beam.end = end - newStart;

                beam.powered = false;

                beam.endPoint = endPoint;
                beam.children = children;

                beam.SetEndpoints();
            }
        }

        children = null;
        endPoint = tile;
        hitPoint = hit.point;
        hitNormal = hit.normal;
        children = tile.OnBeamCollision(this, hit);
        if (children == null) {
            children = new List<BeamHandler>();
        }

        if (tile.TriggersSprayEffect()) {
            // Render a spray effect to highlight that the beam hit a solid, nonreflective surface
            sprayEffect.Play(hit.point, Vector3.Reflect(GetDir(), hit.normal));
        }
    }

    private bool ApproxEqual(Vector3 a, Vector3 b, float prec=1e-3f)
    {
        return (a-b).sqrMagnitude <= prec*prec;
    }

    private void DepowerChildren()
    {
        if (children == null)
            return;
        foreach (var beam in children) {
            game.cleanup += delegate () {
                beam.powered = false;
            };
        }
    }

    private float FindOtherSide(RaycastHit hit)
    {
        // NB: This does not work with concave targets, if we create any of
        // those we need to fix this.  I spent some time trying to come up with
        // a solution for those and got nowhere other than advancing a point
        // until its outside the mesh, but that is slow and not worth doing for
        // now.

        var dir = GetDir();
        var start = hit.point + dir * EPS;

        float dist = 1000;
        RaycastHit hit2;

        hit.collider.Raycast(new Ray(start + dir * dist, -dir), out hit2, dist);
        // Assert.AreEqual(hit.collider, hit2.collider); // vacuously true

        return 1000 - hit2.distance - EPS;
    }

    private void SetEndpoints()
    {
        renderer.SetPosition(0, start * Vector3.right);
        renderer.SetPosition(1, end * Vector3.right);

        collider.center = (end + start) / 2 * Vector3.right;
        collider.height = end - start;

        bool enabled = (end - start) > 0;
        renderer.enabled = enabled;
        collider.enabled = enabled;
    }

    public Vector3 GetPoint(float param = 0)
    {
        return transform.position + param * GetDir();
    }

    public Vector3 GetDir()
    {
        return transform.TransformDirection(1, 0, 0);
    }
}
