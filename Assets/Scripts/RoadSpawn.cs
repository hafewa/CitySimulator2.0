﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class RoadSpawn : MonoBehaviour
{

    [Header("Links required")]
    [Space]
    public GameObject roadChunk;
    public GameObject crossChunk;
    public GameObject CrossChunk3Lane;
    public GameObject curveChunk;
    public GameObject chunkGarage;
    public GameObject crossGarage;

    [Header("Storage for the network")]
    [Space]
    public List<GameObject> allBlocks;
    public List<GameObject> curBlocks;

    [Header("Settings for the lane size")]
    [Space]
    public float outLanesWidth = 4.2f;
    public float innerLanesWidth = 1.8f;

    [Header("Buildings to spawn")]
    [Space]
    public GameObject garbageOffice;
    public GameObject houseBlock;

    private GameObject curPointer;

    private List<GameObject> streetPointsToUpdate;
    private List<GameObject> crossPointsToUpdate;

    private bool deleting = false;

    /// <summary>
    /// Check for the presence of streets and create the network for them
    /// </summary>
    void Start()
    {
        allBlocks = new List<GameObject>();

        // Deactive GrisSnapping
        //for (GameObject g in GameObject.FindGameObjectsWithTag("housingUnit"))
        //    g.GetComponent<GridS>

        NetworkAtStart();
    }

    void Update()
    {
        if (curBlocks.Count == 1)
        {
            var chunketto = curBlocks[0];

            // Rotation
            var scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0)
            {
                if (-0.5f <= chunketto.transform.rotation.eulerAngles.y && chunketto.transform.rotation.y <= 0.5f)
                    chunketto.transform.Rotate(Vector3.up * 90f);
            }
            else if (scroll < 0)
            {
                if (89.5f <= chunketto.transform.rotation.eulerAngles.y && chunketto.transform.rotation.eulerAngles.y <= 90.5f)
                    chunketto.transform.Rotate(Vector3.up * -90f);
            }
        }

        if (deleting == true)
        {
            if (Input.GetMouseButtonDown(0))
            {
                RaycastHit hit;
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out hit, Mathf.Infinity))
                {
                    if (hit.transform.gameObject.tag != "Terrain")
                    Destroy(hit.transform.gameObject);
                }
            }
        }
        
        // Deleting test
        if (Input.GetMouseButtonDown(1) && curBlocks.Count == 0)
            UpdateAllNetwork();
    }

    /// <summary>
    /// Receives responses from buttons depending on button names
    /// </summary>
    public void BuildButtonResponse(string messageType)
    {
        switch (messageType)
        {
            case "Strada UCUCDS":
                if (curBlocks.Count == 0)
                    InitialSpawn();
                break;
            case "Delete Building":
                if (curBlocks.Count == 0)
                    deleting = true;
                break;
            case "Back Deleting":
                deleting = false;
                break;
            case "Garbage Office":
                if (curBlocks.Count == 0)
                    SpawnBuilding(garbageOffice);
                break;
            case "House Block":
                if (curBlocks.Count == 0)
                    SpawnBuilding(houseBlock);
                break;
        }
    }

    /// <summary>
    /// Create a new chunk which is the pointer for the road creation
    /// </summary>
    public void InitialSpawn()
    {
        curBlocks = new List<GameObject>();
        curPointer = Instantiate(roadChunk);
        curPointer.GetComponent<GridSnapping>().enabled = true;
        curPointer.transform.position += Vector3.up * 1;
        curBlocks.Add(curPointer);
    }

    public void SpawnBuilding(GameObject spawnable)
    {
        Instantiate(spawnable);
        spawnable.GetComponent<SpawnBuildingInGrid>().enabled = true;
        spawnable.GetComponent<SpawnBuildingInGrid>().isDragging = true;
    }

    /// <summary>
    /// Destroy the traces and replace them with real road blocks
    /// Starts the coroutine to check for intersections
    /// </summary>
    public void UpdateBlocks()
    {
        streetPointsToUpdate = new List<GameObject>();
        crossPointsToUpdate = new List<GameObject>();

        curBlocks = new List<GameObject>();
        var traces = GameObject.FindGameObjectsWithTag("Trace");
        foreach (GameObject g in traces)
        {
            Destroy(g);
            var curRoadChunk = Instantiate(roadChunk, g.transform.position - Vector3.up * g.transform.position.y, g.transform.rotation, chunkGarage.transform);
            StartCoroutine(DeleteIfColliding(curRoadChunk));
            curBlocks.Add(curRoadChunk);
            if (!allBlocks.Contains(curRoadChunk))
                allBlocks.Add(curRoadChunk);

            // The new chunks need to be updated for the net
            for (int i = 0; i < curRoadChunk.transform.childCount; i++)
                streetPointsToUpdate.Add(curRoadChunk.transform.GetChild(i).gameObject);
        }

        // It was up to be easily clicked
        curPointer.transform.position -= Vector3.up;
        curPointer.GetComponent<GridSnapping>().enabled = false;


        // Complete the road after the street is fully created
        StartCoroutine(CompleteRoad());

        // Cleaning
        curBlocks.Clear();
    }

    /// <summary>
    /// Check for the presence of streets and create the network for them
    /// </summary>
    private void NetworkAtStart()
    {
        streetPointsToUpdate = GameObject.FindGameObjectsWithTag("streetPoint").ToList();
        crossPointsToUpdate = GameObject.FindGameObjectsWithTag("crossPoint").ToList();
        // Curves are treated as cross 
        foreach (GameObject curvePoint in GameObject.FindGameObjectsWithTag("curvePoint"))
            crossPointsToUpdate.Add(curvePoint);

        // First Initialising of the nodes
        foreach (GameObject roadPart in streetPointsToUpdate)
            roadPart.GetComponent<NodeHandler>().InitializeNode();

        foreach (GameObject roadPart in crossPointsToUpdate)
            roadPart.GetComponent<NodeHandler>().InitializeNode();

        UpdateNetwork();
    }

    /// <summary>
    /// After the creation of the real roads it checks for intersections
    /// </summary>
    private IEnumerator CompleteRoad()
    {
        yield return new WaitForFixedUpdate();
        foreach (GameObject block in allBlocks.ToArray())
        {
            if (block == null)
            {
                allBlocks.Remove(block);
                continue;
            }

            // checking for crosses
            var colls = Physics.OverlapSphere(block.transform.position, 0.1f, LayerMask.GetMask("street"));
            if (colls.Length >= 2)
            {
                // Destroying the chunks 
                foreach (Collider c in colls)
                {
                    for (int i = 0; i < c.gameObject.transform.childCount; i++)
                        streetPointsToUpdate.Remove(c.gameObject.transform.GetChild(i).gameObject);
                    Destroy(c.gameObject);
                }

                // Placing the cross prefab
                InstantiateCrossPoint(block.transform.position);

                // Updating the nodes surrounding the cross
                colls = Physics.OverlapSphere(block.transform.position, 15f, LayerMask.GetMask("network"));
                for (int i = 0; i < colls.Length; i++)
                    if (colls[i].gameObject.tag == "streetPoint")
                        streetPointsToUpdate.Add(colls[i].gameObject);
            }
            allBlocks.Remove(block);
        }
        yield return new WaitForFixedUpdate();

        // The roads are now ready to be analysized by the network
        UpdateNetwork();
    }

    /// <summary>
    /// For every road to be updated it calls the update function
    /// </summary>
    private void UpdateNetwork()
    {
        foreach (GameObject streetPoint in streetPointsToUpdate)
            if (streetPoint != null)
                FromStreetPointNodesCreation(streetPoint);
        foreach (GameObject crossPoint in crossPointsToUpdate)
            if (crossPoint != null)
                FromCrossNodesCreation(crossPoint);
    }

    public void UpdateAllNetwork()
    {
        var streetPoints = GameObject.FindGameObjectsWithTag("streetPoint");
        var crossPoints = GameObject.FindGameObjectsWithTag("crossPoint");
        foreach (GameObject streetPoint in streetPoints)
            if (streetPoint != null)
                FromStreetPointNodesCreation(streetPoint);
        foreach (GameObject crossPoint in crossPoints)
            if (crossPoint != null)
                FromCrossNodesCreation(crossPoint);
    }

    /// <summary>
    /// It detects collisions for the given chunck and deletes it
    /// </summary>
    /// <param name="curRoadChunk"></param>
    /// <returns></returns>
    private IEnumerator DeleteIfColliding(GameObject curRoadChunk)
    {
        yield return new WaitForFixedUpdate();

        Debug.Log("Destroy");
        if (curBlocks.Count != 1 & curRoadChunk.GetComponent<CollisionChecking>().isColliding)
            Destroy(curRoadChunk);
    }

    /// <summary>
    /// Given a position it check for what intersection prefab is needed and instantiates it
    /// It also add the cross points to the updateCrossPoints list
    /// </summary>
    /// <param name="pos"></param>
    private void InstantiateCrossPoint(Vector3 pos)
    {
        // Checking number and direction of other roads
        var leftColl = Physics.OverlapSphere(pos + Vector3.left * 14, 0.1f, LayerMask.GetMask("street"));
        var forwardColl = Physics.OverlapSphere(pos + Vector3.forward * 14, 0.1f, LayerMask.GetMask("street"));
        var rightColl = Physics.OverlapSphere(pos + Vector3.right * 14, 0.1f, LayerMask.GetMask("street"));
        var backColl = Physics.OverlapSphere(pos + Vector3.back * 14, 0.1f, LayerMask.GetMask("street"));

        GameObject cross = null;

        if (leftColl.Length == 1 && rightColl.Length == 1 && forwardColl.Length == 1 && backColl.Length == 1)
            cross = Instantiate(crossChunk, pos, Quaternion.identity, crossGarage.transform);

        if (leftColl.Length == 0 && rightColl.Length == 1 && forwardColl.Length == 1 && backColl.Length == 1)
            cross = Instantiate(CrossChunk3Lane, pos, Quaternion.identity, crossGarage.transform);

        if (leftColl.Length == 1 && rightColl.Length == 0 && forwardColl.Length == 1 && backColl.Length == 1)
            cross = Instantiate(CrossChunk3Lane, pos, Quaternion.Euler(0, 180, 0), crossGarage.transform);

        if (leftColl.Length == 1 && rightColl.Length == 1 && forwardColl.Length == 0 && backColl.Length == 1)
            cross = Instantiate(CrossChunk3Lane, pos, Quaternion.Euler(0, 90, 0), crossGarage.transform);

        if (leftColl.Length == 1 && rightColl.Length == 1 && forwardColl.Length == 1 && backColl.Length == 0)
            cross = Instantiate(CrossChunk3Lane, pos, Quaternion.Euler(0, 270, 0), crossGarage.transform);

        //Curve
        // back-left
        if (leftColl.Length == 1 && rightColl.Length == 0 && forwardColl.Length == 0 && backColl.Length == 1)
            cross = Instantiate(curveChunk, pos, Quaternion.Euler(0, 90, 0), crossGarage.transform);
        //back-right
        if (leftColl.Length == 0 && rightColl.Length == 1 && forwardColl.Length == 0 && backColl.Length == 1)
            cross = Instantiate(curveChunk, pos, Quaternion.identity, crossGarage.transform);
        //forward-left
        if (leftColl.Length == 1 && rightColl.Length == 0 && forwardColl.Length == 1 && backColl.Length == 0)
            cross = Instantiate(curveChunk, pos, Quaternion.Euler(0, 180, 0), crossGarage.transform);
        //forward-right
        if (leftColl.Length == 0 && rightColl.Length == 1 && forwardColl.Length == 1 && backColl.Length == 0)
            cross = Instantiate(curveChunk, pos, Quaternion.Euler(0, 270, 0), crossGarage.transform);
        // Adding the cross point 

        for (int i = 0; i < cross.transform.childCount; i++)
            if (cross.transform.GetChild(i).gameObject.tag == "crossPoint")
                crossPointsToUpdate.Add(cross.transform.GetChild(i).gameObject);
        
    }

    /// <summary>
    /// Given a cross it takes its node and check in some check position for connections
    /// </summary>
    /// <param name="cross"></param>
    private void FromCrossNodesCreation(GameObject cross)
    {
        cross.GetComponent<NodeHandler>().InitializeNode();
        var curNode = cross.GetComponent<NodeHandler>().node;

        // all the four positions to check from a cross
        List<Vector3> checkPositions = new List<Vector3> {
            cross.transform.position + (cross.transform.forward * 10),
            cross.transform.position + (cross.transform.forward * 8),
            cross.transform.position + (cross.transform.right  *  -3 - cross.transform.forward * 3)  - Vector3.up*3,
            cross.transform.position + (cross.transform.right  *  -3 - cross.transform.forward * -3) + Vector3.up*3,
            cross.transform.position + (cross.transform.right  *  -3 - cross.transform.forward * 3)  + Vector3.up*3,
            cross.transform.position + (cross.transform.right  *  3 - cross.transform.forward * -3)  + Vector3.up*3,
            cross.transform.position + (cross.transform.right  *  3 - cross.transform.forward * 3)   + Vector3.up*3,
        };
        float radius = 2.5f;
        // if the cross is actually a curve
        if (cross.tag == "curvePoint")
        {
            checkPositions = new List<Vector3> {
            cross.transform.position + (cross.transform.forward * 9)};
        }
        foreach (Vector3 checkPos in checkPositions)
            CheckAtPositionForNodesFromCross(checkPos, curNode, radius);
    }
    
    /// <summary>
    /// Given a streetPoint it takes its node and check in some check position for connections
    /// </summary>
    /// <param name="streetPoint"></param>
    private void FromStreetPointNodesCreation(GameObject streetPoint)
    {
        streetPoint.GetComponent<NodeHandler>().InitializeNode();
        var curNode = streetPoint.GetComponent<NodeHandler>().node;

        // Checking for nodes in front of the current node
        var colls = Physics.OverlapSphere(streetPoint.transform.position + (streetPoint.transform.forward.normalized * 13f), 2.3f, LayerMask.GetMask("network"));
        if (colls.Length > 0)
        {
            CheckAtPositionForNodesFromStreetPoint(colls, streetPoint, curNode);
        }
        else
        {
            colls = Physics.OverlapSphere(streetPoint.transform.position + (streetPoint.transform.forward.normalized * 10f), 2f, LayerMask.GetMask("network"));
            if (colls.Length > 0)
            {
                CheckAtPositionForNodesFromStreetPoint(colls, streetPoint, curNode);
            }
            else
            {   // I m at the end of the street since not nodes in front so I add to possibility to make a u turn
                colls = Physics.OverlapSphere(streetPoint.transform.position - 4 * streetPoint.transform.right, 3f, LayerMask.GetMask("network"));
                CheckAtPositionForNodesFromStreetPoint(colls, streetPoint, curNode);
            }
        }
    }

    private void CheckAtPositionForNodesFromStreetPoint(Collider[] colls, GameObject streetPoint, NodeStreet curNode)
    {
        foreach (Collider c in colls)
        {
            var nextNode = c.gameObject.GetComponent<NodeHandler>().node;
            var curStreet = new ArcStreet(curNode, nextNode);
            curNode.AddStreet(curStreet);
        }
    }

    private void CheckAtPositionForNodesFromCross(Vector3 checkPos, NodeStreet curNode, float radius = 2f)
    {
        var colls = Physics.OverlapSphere(checkPos, radius, LayerMask.GetMask("network"));
        foreach (Collider c in colls)
        {
            var nextNode = c.gameObject.GetComponent<NodeHandler>().node;
            var curStreet = new ArcStreet(curNode, nextNode);
            curNode.AddStreet(curStreet);
        }
    }
}



