﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class VehicleAIController : MonoBehaviour
{
    [Header("Path variables")]
    [Space]
    public List<NodeStreet> waypoints;
    

    [Header("Debug variables")]
    [Space]
    public float rbSpeed;
    public bool stopAtTrafficLight;

    [Header("Settings")]
    [Space]
    public float turningForce;
    public float frontForce;
    public float minTurn;
    public float minTurnToTurn;
    public float minDistanceToCompleteCheck;
    public float raySensorLength;
    public float securityDistance;

    public bool stopped;


    public float maxSpeed;

    public Dictionary<GameObject, bool> trafficLightInfo;

    private Vector3 stopPosForTrafficLight;
    private MotorSimulator motor;
    private List<GameObject> nearbyCars;

    private bool aboutToTurn;

    public NodeStreet nextWaypoint;
    public NodeStreet arrivalNode;


    public Vector3 roadLaneDir;



    void Start()
    {
        roadLaneDir = Vector3.zero;
        stopped = false;
        aboutToTurn = false;
        stopAtTrafficLight = false;
        nearbyCars = new List<GameObject>();
        //waypoints = new List<NodeStreet>();
        maxSpeed = Mathf.Infinity;

        motor = GetComponent<MotorSimulator>();
    }

    void FixedUpdate()
    {
        if (nextWaypoint == null)
            return;


        if (stopped)
        {
            motor.Brake(100000000 * Mathf.Pow(rbSpeed + 1, 6) + 5);
            return;
        }


        // Checking for cars in front 
        var frontDirection = motor.wheel[0].transform.forward;
        var frontPos = transform.position + transform.forward;

        Utils.DrawDebugArrow(frontPos, nextWaypoint.nodePosition - frontPos, Color.green);

        bool goNoProblem = true;
        bool otherCarInFront = false;
        bool otherCarNearby= false;
        var sensorLength = raySensorLength;

        //Debug.Log(waypoints.Count);


        // Checking arrival at waypoint
        var carPos = Utils.Down(frontPos);
        var wayPos = Utils.Down(nextWaypoint.nodePosition);
        var distance = Vector3.Distance(carPos, wayPos);
        if (distance < minDistanceToCompleteCheck)
            StartCoroutine(Recalculating());

        // Checking speed km/h
        rbSpeed = GetComponent<Rigidbody>().velocity.magnitude * 3.6f;

        // Checking how much to turn
        var turning = AngleToTurn();

        // we have a situation to handle
        aboutToTurn = IsAboutToTurn();
        if (stopAtTrafficLight || aboutToTurn)
            goNoProblem = false;

        if (stopAtTrafficLight)
            if (Vector3.Distance(transform.position, stopPosForTrafficLight) < securityDistance+3)
                sensorLength /= 10;

        // Debug sensors
        if (Settings.visualizeVehicleSensors)
        {
            Utils.DrawDebugArrow(frontPos, frontDirection * sensorLength, Color.blue);
            Utils.DrawDebugArrow(frontPos, frontDirection * sensorLength / 6 + transform.right, Color.blue);
            Utils.DrawDebugArrow(frontPos, frontDirection * sensorLength / 6 - transform.right, Color.blue);
            Utils.DrawDebugArrow(frontPos,  transform.right * 1.5f, Color.blue);
            Utils.DrawDebugArrow(frontPos, -transform.right * 1.5f, Color.blue);
        }


        RaycastHit hit = new RaycastHit();
        if (Physics.Raycast(frontPos,
                           frontDirection,
                           out hit,
                           sensorLength,
                           LayerMask.GetMask("vehicle")))
        {
            otherCarInFront = true;
            goNoProblem = false;
        }
        // checking cars at right
        else if (Physics.Raycast(frontPos,
                                 frontDirection * sensorLength / 6 + transform.right,
                                 out hit,
                                 sensorLength/4,
                                 LayerMask.GetMask("vehicle")))
        {
            otherCarNearby = true;
            goNoProblem = false;
            turning -= 0.2f;
        }
        // checking cars at left
        else if (Physics.Raycast(frontPos,
                                 frontDirection * sensorLength / 6 - transform.right,
                                 out hit,
                                 sensorLength / 4,
                                 LayerMask.GetMask("vehicle")))
        {
            otherCarNearby = true;
            goNoProblem = false;
            turning += 0.2f;
        }
        // Checking car on the right
        else if(Physics.Raycast(frontPos,
                                 transform.right,
                                 out hit,
                                 1f,
                                 LayerMask.GetMask("vehicle")))
        {
            otherCarNearby = true;
            goNoProblem = false;
            turning -= 0.3f;
            turning = Mathf.Max(turning, -1);
        }
        // Checking car on the left
        else if (Physics.Raycast(frontPos,
                                 - transform.right,
                                 out hit,
                                 1f,
                                 LayerMask.GetMask("vehicle")))
        {
            otherCarNearby = true;
            goNoProblem = false;
            turning += 0.3f;
            turning = Mathf.Min(turning, 1);
        }



        // Moving forward
        if (goNoProblem)
        {
            // Boosting acceleration if speed is low
            if (rbSpeed < 20)
                frontForce *= 1.1f;

            turningForce = turning * motor.turnPower;
            var force = frontForce * motor.enginePower;
            motor.Move(force, turningForce);
        }
        else
        {
            // Other car in front
            if (otherCarInFront)
            {
                var dist = Vector3.Distance(transform.position, hit.point);
                StoppingProcedure(dist, turning);
            }

            // Red traffic light
            else if (stopAtTrafficLight)
            {
                var dist = Vector3.Distance(transform.position, stopPosForTrafficLight);
                Vector3 ext = Vector3.zero;
                var dir = stopPosForTrafficLight - transform.position;
                if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
                    ext += Vector3.right;
                else
                    ext += Vector3.forward;
                
                var cross = Vector3.Cross(transform.forward, ext);
                StoppingProcedure(dist-1, cross.y);
            }

            // Slowing since I'm about to turn
            else if (aboutToTurn)
            {
                var dist = Vector3.Distance(transform.position, wayPos);
                SlowingProcedure(dist, turning);
            }

            // Slowing and little turning to avoid collision
            else if (otherCarNearby)
            {
                var dist = Vector3.Distance(transform.position, hit.point);
                SlowingProcedure(dist+5, turning);
            }
        }

        // Checking for speed limit
        if (rbSpeed > maxSpeed)
            GetComponent<Rigidbody>().velocity *= 0.99f;

    }

    /// <summary>
    /// A calibrated slowing procedure made to look realistic
    /// </summary>
    /// <param name="dist"></param>
    /// <param name="turning"></param>
    private void SlowingProcedure(float dist, float turning)
    {
        if (dist > securityDistance && rbSpeed > 20)
            motor.Brake(dist * 100000 * Mathf.Pow(rbSpeed + 1, 10) + 3);
        else 
        {
            turningForce = turning * motor.turnPower;
            var force = 1 * motor.enginePower;
            motor.Move(force, turningForce);
        }
    }

    /// <summary>
    /// A calibrated stopping procedure made to look realistic
    /// </summary>
    /// <param name="dist"></param>
    /// <param name="turning"></param>
    private void StoppingProcedure(float dist, float turning)
    {
        if (dist > securityDistance && rbSpeed > 20)
            motor.Brake(dist * 100000000 * Mathf.Pow(rbSpeed + 1, 6) + 3);
        else if (dist > securityDistance)
        {

            turningForce = turning * motor.turnPower;
            var force = 1 * motor.enginePower;

            if (turning > 0.3f)
                force /= 2;

            motor.Move(force, turningForce);
        }
        else
        {
            motor.Brake(dist * 100000000 * Mathf.Pow(rbSpeed + 1, 6) + 3);
        }
    }

    /// <summary>
    /// Called by the traffic light zone to inform the car if it has to stop
    /// </summary>
    /// <param name="stopPos"></param>
    public void StopAtTrafficLight(Vector3 stopPos, bool stop)
    {
        if (stop)
        {
            this.stopAtTrafficLight = true;
            stopPosForTrafficLight = stopPos;
        }
        else
            this.stopAtTrafficLight = false;
    }

    /// <summary>
    /// Positive value: turn right, Negative value: turn left
    /// </summary>
    /// <returns></returns>
    private float AngleToTurn()
    {
        if(waypoints.Count == 0) { return 0; }
        var heading = waypoints[0].nodePosition - transform.position;
        var cross =  Vector3.Cross(transform.forward, heading.normalized);

        if (Mathf.Abs(cross.y) < minTurnToTurn)
            return 0;


        return cross.y;
    }

    /// <summary>
    /// Check if the car is about to turn so that I can slow down
    /// </summary>
    /// <returns></returns>
    private bool IsAboutToTurn()
    {
        if (waypoints.Count <= 1)
            return false;

        var heading = waypoints[1].nodePosition - transform.position;
        var cross = Vector3.Cross(transform.forward, heading.normalized).y;

        if (Mathf.Abs(cross) < minTurn)
            return false;

        return true;
    }


    private void SensorActivation()
    {

    }



    /// <summary>
    /// Recalculating the next target after arrival at one
    /// if the path is completed then autodestruction is performed
    /// </summary>
    /// <returns></returns>
    public abstract IEnumerator Recalculating();


    



    // TODO : dynamic pathfinding to avoid traffic
}
