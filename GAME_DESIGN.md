# Fly Me to the Moon: game design

Updated 2026-11-14

## How the game looks

There is no 3D environment. The player only ever sees two things:

- **3D objects** they can grab: the countdown timer, the bed, the five items, and the two choices each turn.
- **A Visko video** on the curved screen, which provides all the live visuals for every scene.

Any place named in this doc (the dorm, the campus, the hangar, the Moon) appears in the Visko video and is not built in Unity.

Two meters stay on screen for the whole game: **distance to the Moon** and **wellness**. A light flashes green, yellow or red after every choice.

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

Each item is single-use. It gets the player past one distraction for free, and the game master can also offer a clever choice that spends an item the player holds.

| Item | Solves |
| --- | --- |
| Alarm clock | Heat nap |
| Red Bull can | Heat nap or Moon customs |
| Flashlight | Blackout |
| CS textbook | Blackout |
| Mystery paper bag | Moon customs |

## The eight beats

The order is fixed. The AI writes the scenes and choices inside it.

| # | Beat | Type |
| --- | --- | --- |
| 1 | Roommate | Fixed decision |
| 2 | Escape campus | AI decision, encounter |
| 3 | Heat nap | Distraction |
| 4 | Build the rocket | AI decision |
| 5 | Blackout | Distraction |
| 6 | In space | AI decision, encounter |
| 7 | Moon customs | Distraction |
| 8 | Final approach | AI decision |

Then the ending.

### Beat 1: your roommate is asleep through the apocalypse

| Choice | Result | Distance | Wellness | Light |
| --- | --- | --- | --- | --- |
| Wake him up | Kyle comes along | 0 | 0 | Green |
| Leave him | You save time, and he turns up again at the ending | 0 | -2% | Red |

## How decisions work

Every AI decision gives the player exactly two choices, shown as two grabbable 3D objects.

- Choices address the player as "you" and are big decisions, never small ones like left or right.
- At least one choice refers to something that happened earlier.
- The pairing varies: one kind and one selfish, two selfish, two kind, or a clever option that spends an item.
- **Encounters** (beats 2 and 6): someone or something shows up to block, rob or threaten the player, and both choices are ways to deal with it, such as fight, outsmart, bargain or help.
- **The past catches up.** About one turn in three, someone the player helped returns the favor, or someone they wronged gets revenge.

## Scoring

The AI judges each choice. The code does the math.

For each choice the AI sets:

- **Speed, 1 to 10:** how much closer the choice gets the player.
- **Wellness change, -12 to +5:** harming, abandoning or cheating someone costs more the worse it is. Honest or generous acts that cost the player something earn 0 to +5.
- **A short reason** shown next to the meter, such as "-7% for selling Kyle's mattress."

Distance uses one formula: `miles = beat size x (0.7 + 0.06 x speed)`

| Beat | Beat size | Speed 1 | Speed 10 |
| --- | --- | --- | --- |
| Escape campus | 6,000 | 4,560 | 7,800 |
| Build the rocket | 130,000 | 98,800 | 169,000 |
| In space | 62,000 | 47,120 | 80,600 |
| Final approach | 38,000 | 28,880 | 49,400 |

Other rules:

- Normally the faster choice costs more wellness. A choice that is better on both counts is allowed once per game, as a payoff for earlier kindness.
- Wellness only rises when the player picks a positively scored choice. It never recovers on its own, and it caps at 100%.
- Light: green for a wellness change of 0 or more, yellow for -1 to -4, red for -5 or worse.
- The narration never states numbers. The meters do that.

## Distractions

Three distractions interrupt the player. They resolve automatically from the items the player holds.

| Distraction | What happens | Items that solve it | With no item |
| --- | --- | --- | --- |
| Heat nap | It's so hot your body shuts down and the screen fades to black | Alarm clock or Red Bull | -5% wellness |
| Blackout | The grid melts just before launch and the launch computer crashes | Flashlight or CS textbook | 20-second delay |
| Moon customs | A raccoon in a tiny uniform says "Snacks or turn around" | Mystery bag or Red Bull | +20,000 miles |

## Endings

The ending depends on two things: whether the player reached the Moon, and whether wellness is 85% or higher.

| | Wellness 85% or higher | Wellness below 85% |
| --- | --- | --- |
| **Reached the Moon** | Moon Citizen | Moon Reject |
| **Fell short** | Rescued (within 40,000 miles) or Too Far to Tow | Stranded |

- **Moon Citizen:** "You made it, and so did everyone you stopped for. The Moon is 72 degrees. Someone saved you a seat."
- **Moon Reject:** "You made it. The Moon residents' board reviewed your journey and voted no. You may use the parking lot."
- **Rescued:** "Your fuel runs out. Then a tow line hits your window. It's someone you helped, and they brought snacks."
- **Too Far to Tow:** "Your fuel runs out a long way from anywhere. People you helped wave from their windows as they pass."
- **Stranded:** "Your fuel runs out. You call everyone you met. It rings, and rings, and goes to voicemail."

One Kyle line is added to every ending:

| | Woke Kyle | Left Kyle |
| --- | --- | --- |
| **On the Moon** | "Kyle climbs out behind you and asks if there's Wi-Fi." | "Kyle is already here, holding a smoothie." |
| **Still in space** | "Kyle floats beside you, still asleep." | "Through the window you can see Kyle on the Moon, waving." |

## How it runs

- **Unity (Quest)** shows the video, the meters and the two 3D choices, and reports which choice was grabbed.
- **The game server** (Python, FastAPI) holds the scores and items, enforces the beats, and asks the AI for each turn.
- **The AI** writes the narration, the two choices, their scores, and one Visko prompt per choice.
- **Visko** turns the chosen prompt into the video. Unity sends the prompt using the existing Visko script.

Every Visko prompt starts with the same style line and describes "the student" in third person, with no dialogue or on-screen text.

## Still to decide

- [ ] Whether 85% is the right wellness line, after a few test games.
- [ ] Whether a timer runs after the 30-second grab, or the game is untimed from there.
- [ ] The reveal screens after the ending: what the lights meant, how the player compares to others, travel type.
- [ ] Demo length: how many beats to show in three minutes.
