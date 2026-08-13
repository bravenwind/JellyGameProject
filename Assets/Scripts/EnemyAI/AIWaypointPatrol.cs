using System.Collections;
using UnityEngine;

public class AIWaypointPatrol : JellyAgentAI
{
    [Header("Settings")]
    public Transform[] waypoints;
    public float waitTime = 1.0f;

    private int currentIndex;
    private bool movingForward = true;

    protected override bool IsReady()
    {
        return waypoints != null && waypoints.Length > 0 && waypoints[0] != null;
    }

    protected override void OnBecameDriver()
    {
        MoveToNextWaypoint();
    }

    protected override void DriveUpdate()
    {
        if (isWaiting)
            return;

        if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance)
            return;

        StartCoroutine(WaitAndMove());
    }

    private IEnumerator WaitAndMove()
    {
        isWaiting = true;

        agent.isStopped = true;
        agent.velocity = Vector3.zero;

        yield return new WaitForSeconds(waitTime);

        agent.isStopped = false;
        isWaiting = false;

        MoveToNextWaypoint();
    }

    //끝에 닿으면 방향을 뒤집어 왕복한다
    private void MoveToNextWaypoint()
    {
        agent.destination = waypoints[currentIndex].position;

        if (movingForward && currentIndex >= waypoints.Length - 1)
            movingForward = false;
        else if (!movingForward && currentIndex <= 0)
            movingForward = true;

        currentIndex += movingForward ? 1 : -1;
        currentIndex = Mathf.Clamp(currentIndex, 0, waypoints.Length - 1);
    }
}
