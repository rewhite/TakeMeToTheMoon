# Fly Me to the Moon: game design

As of 2026-11-13

## How the game looks

There is no 3D environment. The player only ever sees two things:

- **3D objects** they can grab: the countdown timer, the bed, the five items, and later the choices.
- **A Visko video** on the curved screen, which provides all the live visuals for every scene.

Any place named in this doc (the dorm, the campus, the hangar, the Moon) appears in the Visko video and is not built in Unity.

## Before the game: the opening

The player is jolted awake in a Stanford dorm room, shown in the Visko video. Then the countdown begins and the narration pops up.

> "Breaking news. Earth has officially hit 451 degrees. Scientists say we had 'plenty of warnings' and 'one job.' The oceans are a hot tub. Lake Lag has boiled, which is impressive, because it was already empty. The Moon is now the only place with decent weather. You have thirty seconds to grab what you can. Good luck, and stay hydrated."

| Meter | Starting value |
| --- | --- |
| Distance to the Moon | 238,900 miles |
| Wellness | 100% |
| Timer | 0:30 |

## Initial screen: grab three items

The player sees a 3D countdown timer set to thirty seconds and five 3D objects sitting on a 3D bed. They grab three of the five before the timer runs out.

Each item is a single-use power that gets the player past a distraction instantly. Some can be spent on the rocket instead.

| Item | Solves | Can also be spent as |
| --- | --- | --- |
| Alarm clock | Distraction 1 | nothing |
| Red Bull can | Distractions 1 and 3 | the best rocket fuel |
| Flashlight | Distraction 2 | nothing |
| CS textbook | Distraction 2 | pilot upgrade (you can actually fly) |
| Mystery paper bag | Distraction 3 | a random nose cone |

## Stage 1: escape campus

Two quick decisions. Only the first is written so far.

**Decision 1: your roommate is asleep through the apocalypse.**

| Choice | Result | Meter | Light |
| --- | --- | --- | --- |
| Wake him up | He comes along and is available as a rocket part later; costs 2% of wellness score | 0 | Green |
| Leave him | You save time, but he reappears on the Moon, having beaten you there | 0 | Red |

## Distractions

Three distractions interrupt the player along the way. The right item solves each one instantly.

| Distraction | What happens | Items that solve it | With no item |
| --- | --- | --- | --- |
| 1. Heat nap | It's so hot your body shuts down, the screen fades to black, and the timer keeps running | Alarm clock or Red Bull: you wake instantly | You lose 5% of your wellness score |
| 2. Blackout | The grid melts just before launch, the hangar goes dark, and the launch computer crashes | Flashlight: you find the reset button. CS textbook: you debug it, or burn the book for light and lose it as a pilot upgrade | You feel around in the dark for 20 seconds |
| 3. Moon customs | A raccoon in a tiny uniform stops your rocket and says "Snacks or turn around" | Mystery bag: it was a dining hall burrito, and he waves you through. Red Bull: he takes it and now has wings | He fines you 20,000 miles |

## Still to decide

- [ ] The final power of each item.
- [ ] The narration now says "thirty seconds" to match the new timer. Confirm, or keep "sixty".
- [ ] Whether a timer keeps running after the 30-second grab. Distractions 1 and 2 both assume one does.
- [ ] Decision 2 of Stage 1.
- [ ] The rocket build, the launch, and the endings.
