using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.RTS.Core;
using UnityLab.RTS.Units;

namespace UnityLab.RTS.World
{
    public enum ResourceType
    {
        Stone,
        Wood,
    }

    /// <summary>
    /// A mine or a tree: a finite stock of one resource with a ring of harvesting spots around it.
    /// Spots are handed out by <see cref="HarvestSpots{T}"/>; harvesters that find no free spot wait
    /// in a queue and are called in when one frees up. Small markers show which spots are taken.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class ResourceNode : MonoBehaviour
    {
        [FormerlySerializedAs("resource")]
        [SerializeField] ResourceType type = ResourceType.Stone;
        [FormerlySerializedAs("totalResources")]
        [SerializeField, Min(0)] int amount = 10000;
        [FormerlySerializedAs("totalHarvestingSpots")]
        [SerializeField, Min(1)] int spotCount = 8;
        [Tooltip("How far outside the collider the spots are placed.")]
        [SerializeField] float spotMargin = 1f;

        static Material freeMarker, takenMarker;

        ResourceStock stock;
        HarvestSpots<HarvesterUnit> spots;
        Vector3[] spotPositions;
        Renderer[] markers;

        public ResourceType Type => type;
        public int Remaining => stock.Remaining;
        public bool IsDepleted => stock.IsDepleted;

        /// <summary>Squared distance at which a harvester stops walking and asks for a spot.</summary>
        public float ArrivalDistanceSquared { get; private set; }

        void Awake()
        {
            stock = new ResourceStock(amount);
            spots = new HarvestSpots<HarvesterUnit>(spotCount);
            CreateSpots();
        }

        void CreateSpots()
        {
            if (freeMarker == null)
            {
                Shader shader = Shader.Find("Standard");
                freeMarker = new Material(shader) { color = new Color(1f, 1f, 1f, 1f), hideFlags = HideFlags.DontSave };
                takenMarker = new Material(shader) { color = new Color(0.95f, 0.36f, 0.33f, 1f), hideFlags = HideFlags.DontSave };
            }

            var sphere = GetComponent<SphereCollider>();
            float radius = sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) + spotMargin;
            spotPositions = new Vector3[spotCount];
            markers = new Renderer[spotCount];
            for (int i = 0; i < spotCount; i++)
            {
                float angle = i * Mathf.PI * 2f / spotCount;
                spotPositions[i] = transform.position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;

                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "Harvest spot " + i;
                Destroy(marker.GetComponent<Collider>());
                marker.transform.SetParent(transform, true);
                marker.transform.position = spotPositions[i];
                marker.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
                markers[i] = marker.GetComponent<Renderer>();
                markers[i].sharedMaterial = freeMarker;
            }

            float arrival = radius + spotMargin * 2f;
            ArrivalDistanceSquared = arrival * arrival;
        }

        /// <summary>Asks for a spot.</summary>
        /// <returns>True with the spot position, or false if the harvester was queued.</returns>
        public bool RequestSpot(HarvesterUnit harvester, out Vector3 position)
        {
            int spot = spots.Request(harvester);
            position = spot >= 0 ? spotPositions[spot] : Vector3.zero;
            RefreshMarkers();
            return spot >= 0;
        }

        /// <summary>Frees the harvester's spot (or queue place) and calls in the next waiting harvester.</summary>
        public void Leave(HarvesterUnit harvester)
        {
            int spot = spots.Leave(harvester, out HarvesterUnit next);
            if (spot >= 0 && next != null)
                next.OnSpotAssigned(this, spotPositions[spot]);
            RefreshMarkers();
        }

        /// <returns>The amount actually taken, less than asked when the node runs out.</returns>
        public int Take(int requested)
        {
            int taken = stock.Take(requested);
            if (stock.IsDepleted)
                OnDepleted();
            return taken;
        }

        void OnDepleted()
        {
            // Nothing left: shrink the node so players can see it is exhausted.
            transform.localScale *= 0.5f;
        }

        void RefreshMarkers()
        {
            for (int i = 0; i < markers.Length; i++)
                markers[i].sharedMaterial = spots.IsTaken(i) ? takenMarker : freeMarker;
        }
    }
}
