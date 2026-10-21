namespace Marey

open System
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols

// ======================================================================================
// EVENTS.
//
// An event is a function of the state, a direction and a response. Within a step whose ends
// straddle a root in the stated direction, the root is located to a stated tolerance in time:
// for an adaptive step on its dense output, for a fixed step by re-stepping from the step's start
// with a shorter `h` (the Illinois variant of regula falsi). The response is applied there, and
// integration restarts from the event. The earliest root in a step wins; the rest are found again
// in what remains of it.
//
// The root is taken on the side the sign has already crossed to, so the restarted step cannot
// find the same crossing a second time.
//
// **Zeno.** A ball with restitution below one bounces infinitely often before a finite time. An
// event may carry a minimum separation: a crossing closer than that to the event's previous one
// fires the event's rest response if it has one (a ball at rest stays on the floor), and stops
// the simulation with the reason if it has not. Nothing loops.
//
// What is not found: a step whose two ends are on the same side of a root it crossed twice. That
// is the step size's business, as it is for every event-locating integrator.
// ======================================================================================

/// Which crossings of zero an event fires on.
[<RequireQualifiedAccess>]
type Direction =
    /// From negative to zero or positive.
    | Rising
    /// From positive to zero or negative.
    | Falling
    /// Either.
    | Either

/// What happens at an event.
[<RequireQualifiedAccess>]
type Response<'s> =
    /// The state is replaced, and integration goes on from it.
    | Continue of ('s -> 's)
    /// The simulation stops; past the event its state holds.
    | Stop

/// A crossing of zero by a function of the state, and what to do there. Built by
/// `Event.create`, which takes the function in its own unit.
type Event<'s> =
    { Name: string
      /// The event function, its unit erased.
      Of: 's -> float
      Direction: Direction
      Response: Response<'s>
      /// What a crossing too soon after the previous one does instead: the guard against Zeno
      /// behaviour.
      Rest: ('s -> 's) option
      /// The least time between two firings before the guard takes over.
      MinSeparation: float<s> option
      /// The time a root is located to, as a fraction of the step it was found in.
      Tolerance: float }

/// What happened at an event, and when.
[<RequireQualifiedAccess>]
type HitKind =
    /// The event's response was applied.
    | Fired
    /// A crossing came sooner than the event's minimum separation, and its rest response was
    /// applied instead.
    | Rested
    /// The simulation stopped here, for the reason given.
    | Stopped of reason: string

/// One event the simulation passed.
type Hit =
    { Name: string
      Time: float<s>
      Kind: HitKind }

[<RequireQualifiedAccess>]
module Event =

    /// An event on `g`, which may be in any unit: only its sign matters, and the root is located
    /// in time. The default tolerance locates a root to `10⁻⁹` of the step it fell in.
    let create<'s, [<Measure>] 'g> (name: string) (direction: Direction) (response: Response<'s>) (g: 's -> float<'g>) : Event<'s> =
        { Name = name
          Of = fun s -> float (g s)
          Direction = direction
          Response = response
          Rest = None
          MinSeparation = None
          Tolerance = 1e-9 }

    /// The Zeno guard: a crossing within `separation` of the previous one applies `rest` instead
    /// of the response.
    let withRest (separation: float<s>) (rest: 's -> 's) (e: Event<'s>) : Event<'s> =
        if not (separation > 0.0<s>) then
            invalidArg "separation" "Marey: an event's minimum separation must be positive."

        { e with
            Rest = Some rest
            MinSeparation = Some separation }

    /// The Zeno guard without a rest response: a crossing within `separation` of the previous one
    /// stops the simulation, saying why.
    let withMinSeparation (separation: float<s>) (e: Event<'s>) : Event<'s> =
        if not (separation > 0.0<s>) then
            invalidArg "separation" "Marey: an event's minimum separation must be positive."

        { e with MinSeparation = Some separation }

    /// The tolerance a root is located to, as a fraction of the step it fell in.
    let withTolerance (fraction: float) (e: Event<'s>) : Event<'s> =
        if not (fraction > 0.0 && fraction < 1.0) then
            invalidArg "fraction" "Marey: an event's tolerance is a fraction of the step, between 0 and 1."

        { e with Tolerance = fraction }

    /// Whether `g0` to `g1` is a crossing in `direction`. A start exactly on zero is not one: the
    /// root it sits on was found already.
    let crosses (direction: Direction) (g0: float) (g1: float) =
        match direction with
        | Direction.Rising -> g0 < 0.0 && g1 >= 0.0
        | Direction.Falling -> g0 > 0.0 && g1 <= 0.0
        | Direction.Either -> (g0 < 0.0 && g1 >= 0.0) || (g0 > 0.0 && g1 <= 0.0)

    /// The Illinois variant of regula falsi on `[a, b]`, where `ga` and `gb` have the signs a
    /// crossing has at its two ends: the right end of the last bracket, so on the crossed side,
    /// once the bracket is narrower than `tol`.
    let locate (phi: float -> float) (a: float) (b: float) (ga: float) (gb: float) (tol: float) : float =
        let mutable a = a
        let mutable b = b
        let mutable ga = ga
        let mutable gb = gb
        let mutable side = 0
        let mutable iterations = 0

        while b - a > tol && gb <> 0.0 && iterations < 200 do
            iterations <- iterations + 1
            let c = (a * gb - b * ga) / (gb - ga)
            // A secant point on an end, from an exhausted bracket, is replaced by the midpoint.
            let c = if c <= a || c >= b || Double.IsNaN c then 0.5 * (a + b) else c
            let gc = phi c

            if (gc > 0.0) = (gb > 0.0) && gc <> 0.0 then
                // The crossed side moved in; halve the stale end's weight (Illinois).
                b <- c
                gb <- gc

                if side = -1 then
                    ga <- 0.5 * ga

                side <- -1
            elif gc = 0.0 then
                b <- c
                gb <- 0.0
            else
                a <- c
                ga <- gc

                if side = 1 then
                    gb <- 0.5 * gb

                side <- 1

        b

[<RequireQualifiedAccess>]
module Response =

    /// Reflect coordinate `axis` of a second-order state: its velocity reversed and scaled by the
    /// restitution `e` (1 elastic, 0 perfectly inelastic). A ball meeting the floor at `q.[1] = 0`
    /// is `Event.create "floor" Direction.Falling (Response.reflect 1 0.8) (fun (struct (q, _)) -> q.[1])`.
    let reflect<[<Measure>] 'q, [<Measure>] 't> (axis: int) (e: float) : Response<struct (float<'q>[] * float<'q / 't>[])> =
        if not (e >= 0.0 && e <= 1.0) then
            invalidArg "e" (sprintf "Marey: a restitution is between 0 and 1, not %g." e)

        Response.Continue(fun (struct (q, v)) ->
            let v = Array.copy v
            v.[axis] <- -e * v.[axis]
            struct (Array.copy q, v))
