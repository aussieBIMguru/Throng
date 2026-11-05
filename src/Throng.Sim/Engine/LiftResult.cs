namespace Throng.Sim.Engine;

/// <summary>
/// One lift bank's results after the warm-up (round 015, D-260): people who boarded, their landing waits, ride times,
/// and people who ran out of patience on its landings and went another way (D-253).
/// </summary>
/// <param name="Lift">Index into <see cref="SimModel.Lifts"/>.</param>
/// <param name="Name">Bank name.</param>
/// <param name="Cars">Cars in the bank.</param>
/// <param name="Boarded">People who boarded a car.</param>
/// <param name="MeanWaitS">Mean landing wait of those who boarded, seconds.</param>
/// <param name="P95WaitS">95th percentile landing wait, seconds.</param>
/// <param name="MaxWaitS">Longest landing wait, seconds.</param>
/// <param name="Rides">People who got off a car.</param>
/// <param name="MeanRideS">Mean time from boarding to getting off, seconds.</param>
/// <param name="GaveUp">People who gave up on its landings and went another way.</param>
public readonly record struct LiftResult(
    int Lift, string Name, int Cars, long Boarded, double MeanWaitS, double P95WaitS, double MaxWaitS, long Rides, double MeanRideS,
    long GaveUp);
