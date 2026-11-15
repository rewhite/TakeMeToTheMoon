# Take Me to the Moon: game design

Updated 2026-11-15. Built for Immerse the Bay 2026, whose theme is "Take Me to the Moon."

## The idea

Everyone reaches the Moon. Not everyone is welcome there.

The player escapes an overheating Earth through six checkpoints. The first five each hold a decision or a challenge, and the sixth is the landing on the Moon, where the game ends. A wellness score tracks how they treated the people around them. At the end, the people already on the Moon decide whether to accept them.

There is no distance meter and no running clock after the opening. What the game measures is who the player was on the way.

## Worldview and tone

- **Setting:** the near future, on the day the heat stops being survivable. No year is given. The game opens at 2 a.m. in a Stanford dorm room.
- **Tone:** tense survival with dry, dark humor. The danger is real and people are scared. The game never makes light of people in danger.
- **Theme:** "When everything is on fire, who are you?"

Rules of the world:

1. **The danger is real and getting worse.** Each checkpoint is hotter and more broken than the last.
2. **There is not enough for everyone.** Seats, fuel, water and time are all short, so every decision takes something from someone.
3. **People remember.** Anyone the player helps or abandons can come back later.
4. **The Moon is a community, not a prize.** The people there judge the player by how they arrived.
5. **No authorities.** There are no customs, police, officers, inspectors, drones or raccoons anywhere in the game.

## How the game looks

There is no 3D environment. The player sees two things:

- **3D objects** they grab with their hands: the countdown timer, the five items on the bed, the two choices at each decision, and their remaining items during an emergency.
- **A Visko video** on the curved screen, generated live, which provides the visuals for every scene.

A text box above the two choices shows the current narration. A light flashes green, yellow or red after each choice.

## Opening

### The broadcast (about 20 seconds)

The player is in bed when a siren starts and an emergency broadcast plays in the room.

> "This is an emergency broadcast. This is not a test. Surface temperature has passed 451 degrees and is rising. All residents must evacuate. The final launches for the Moon depart within the hour. Seating is limited. Grab three items from in front of you and leave immediately."

Sound: three alert tones, then the siren under a calm, flat voice. The signal cuts to static at the end.

### Intro video: five shots of about four seconds

| Shot | Narration over it | What we see |
| --- | --- | --- |
| 1 | "This is an emergency broadcast. This is not a test." | A news anchor at her desk in a studio washed in red emergency light |
| 2 | "Surface temperature has passed 451 degrees and is rising." | A jammed highway at sunset, abandoned cars sinking into melting asphalt |
| 3 | "All residents must evacuate." | Crowds running down a city street through smoke and flashing red lights |
| 4 | "The final launches for the Moon depart within the hour. Seating is limited." | Rockets rising on the horizon while a crowd presses against a gate |
| 5 | "Grab three items from in front of you and leave immediately." | The anchor looks into the camera, the studio lights fail, the feed breaks into red static |

Mood: urgent, apocalyptic, red and orange. The intro is generated once and saved, since it never changes. A separate first-person clip shows the dorm room from the bed: the blanket in the foreground, the roommate asleep across the room, and the window glowing orange.

### Grab three items

A 3D countdown timer of 15 seconds appears with five 3D objects on the bed. The player takes three.

On-screen instruction (proposed): **"Grab 3 items. You can't carry more."** with a counter, "0 of 3". The game does not explain what the items do.

| Item | Gets the player through |
| --- | --- |
| Alarm clock | Heat nap |
| Red Bull can | Heat nap or engine stall |
| Flashlight | Blackout |
| CS textbook | Blackout |
| Mystery paper bag | Engine stall |

Each item is single-use.

## The six checkpoints

Each checkpoint has a fixed location. Scenes 2 and 4 are written live by the AI, scenes 1 and 5 are fixed, scene 3 is a mini game, and scene 6 is the landing.

| # | Checkpoint | Decision | Emergency after it |
| --- | --- | --- | --- |
| 1 | Dorm Room | Roommate (fixed) | none |
| 2 | Campus | Encounter | Heat nap |
| 3 | Launch Site | Throw mini game | Blackout |
| 4 | Low Orbit | Encounter | Engine stall |
| 5 | Moon Approach | Kyle (fixed, depends on scene 1) | none |
| 6 | The Moon | None. The player lands and the game ends with the verdict | none |

### Checkpoint 1: the roommate

Your roommate, Kyle, is asleep through the evacuation.

| Choice | Result | Wellness |
| --- | --- | --- |
| Wake him up | Kyle comes with you | +5 |
| Leave him | You get out faster, and he comes back at the end | -5 |

### Checkpoint 3: the throw

The player throws a 3D object at a target and has three tries. Hitting the target within three tries is +5. Missing all three is -5.

### Checkpoint 5: Moon Approach

This is the last decision of the game. It is fixed, not written by the AI. Which scene the player gets depends on what they did with Kyle in scene 1.

**If the player left Kyle: the call from Earth.**
"The radio crackles. It's Kyle, still on Earth, asking if anyone can hear him. Your ship has one escape pod."

| Choice | Outcome | Wellness |
| --- | --- | --- |
| Send the pod for Kyle | "You launch the pod toward Earth and bring the ship down hard without it." | +5 |
| Switch off the radio | "The radio goes quiet. You land smoothly, and nobody has asked about him yet." | -5 |

**If the player woke Kyle: one seat on the lander.**
"Your rocket can't land. The last lander has one seat left this trip, and Kyle is standing next to you."

| Choice | Outcome | Wellness |
| --- | --- | --- |
| Give Kyle the seat | "Kyle goes down first. You wait alone in orbit for the lander to come back." | +5 |
| Take the seat yourself | "You land first. Kyle watches from the window and waits for the next trip." | -5 |

### Checkpoint 6: The Moon

There is no decision here. The outcome of the Kyle decision plays as the landing video, the player arrives on the Moon, and the game ends with the verdict.

## How decisions work

- Every decision offers exactly two choices, shown as two grabbable 3D objects.
- Narration and labels address the player as "you". Labels start with a verb and are at most six words.
- Choices are big dilemmas, never small ones like left or right. Helping someone costs the player something, and helping yourself costs someone else.
- **Encounters** (Campus and Low Orbit): someone or something blocks, robs or threatens the player, and both choices are ways to deal with it. In space these are other escapees, stowaways or rivals, never anyone in authority.
- **The past catches up.** About one turn in three, someone the player helped returns the favor, or someone they wronged gets revenge.
- Every decision has exactly one good choice and one bad choice. The player is not told which is which, so the bad choice has to be tempting: safer, faster, or more loyal to someone the player cares about.

Narration limits, so it reads at a glance in the text box:

- Outcome: one sentence, at most 20 words.
- Next scene: at most two sentences and 30 words. The first says what is in front of you, the second states the problem.
- One thing happens per scene, with at most one new character.

## Wellness

### What it is

A single score from 0 to 100. Every player starts at 100. It measures how well the player comes through the journey: how they treat other people, and how they handle what goes wrong.

### What it is for

Everyone reaches the Moon, so arriving is not the win. The wellness score is what the people on the Moon judge the player by, and it decides the ending.

### How it is calculated

Every good decision is +5 and every bad decision is -5. The AI decides which choice is good and which is bad, and the code applies the points.

| Where | Good | Bad |
| --- | --- | --- |
| Scene 1, roommate | Wake him: +5 | Leave him: -5 |
| Scenes 2 and 4 (AI-written) | The choice the AI labels good: +5 | The choice it labels bad: -5 |
| Scene 3, the throw | Hit within three tries: +5 | Miss all three: -5 |
| Scene 5, Kyle | Generous choice: +5 | Self-first choice: -5 |
| Three emergencies | Right item: no change | Wrong item or none: -5 |

- Good means treating other people fairly or generously, even at a cost to yourself. Bad means putting yourself first at someone else's expense.
- The score cannot go above 100 or below 0. Because of the cap, a good decision made at 100 earns nothing. Players can earn back points they have lost, but cannot save them up in advance.
- The score never recovers on its own.
- Light: green for good, red for bad, yellow for a failed emergency.

### The threshold

| Final score | Result |
| --- | --- |
| 85 or above | Pass. The Moon accepts you. |
| Below 85 | Fail. You arrive, and you are not admitted. |

These are the only two outcomes. In plain terms, a player can afford three mistakes, and a fourth costs them the Moon unless they have earned one back.

## Emergencies

Three emergencies interrupt the journey. Each is its own step: the video shows it, the player's remaining items appear as 3D objects, and the player grabs one.

| Emergency | After | Solved by | Wrong item or none |
| --- | --- | --- | --- |
| Heat nap | Campus | Alarm clock or Red Bull | -5% wellness |
| Blackout | Launch Site | Flashlight or CS textbook | -5% wellness |
| Engine stall | Low Orbit | Red Bull or mystery bag | -5% wellness |

The right item is used up. A wrong item is kept, and the penalty applies.

## Verdict

Everyone arrives. The verdict depends only on the final wellness score.

### Players who pass (85 or above): the five archetypes

Each passing player is given one traveler archetype. The code checks them in this order and assigns the first that fits, so two players who made the same choices always get the same type.

| # | Archetype | Who gets it | What the player reads |
| --- | --- | --- | --- |
| 1 | The Guardian | Made no bad decisions at all | "You never put yourself first. Everyone you met made it because of you." |
| 2 | The Redeemed | Left Kyle at the start, then sent the pod back for him at the end | "You made a mistake in the first minute and spent the rest of the journey making it right." |
| 3 | The Loyal One | Woke Kyle and gave him the seat, but made a bad decision toward a stranger | "You would do anything for your own people. Strangers had to earn it." |
| 4 | The Prepared | Got through all three emergencies with the right item | "You packed for the worst and it came. Nothing on the way caught you off guard." |
| 5 | The Survivor | Everyone else who passed | "You weren't perfect, and you knew it. You did enough good to be let in." |

The AI adds one sentence under the description that names something this player did.

### The end page

```
              THE REDEEMED

  You made a mistake in the first minute and spent
  the rest of the journey making it right.

              Wellness  90 / 100

  Your decisions
  Dorm Room       Left your roommate            -5
  Campus          Carried a stranger's child    +5
  Launch Site     Hit the target                +5
  Low Orbit       Took the last oxygen tank     -5
  Moon Approach   Sent the pod for Kyle         +5
  Emergency       No item for the blackout      -5
```

- The archetype title is the largest thing on the page, with its description under it.
- The wellness score comes next.
- The decision list has one row for each of the five scored checkpoints, with what the player chose and the points. The code records these as the player plays. Emergencies appear only if the player lost points on one.

### Players who fail (below 85)

They see a short screen with no archetype:

> **Not Admitted.** "You made it to the Moon. The people here know what you did to get a seat."

followed by their wellness score.

### Kyle at the end

One line about Kyle is added, depending on the final decision:

| Final choice | Kyle line |
| --- | --- |
| Sent the pod | "Kyle lands an hour after you. He doesn't say anything. He just nods." |
| Switched off the radio | "Someone asks if you knew a Kyle. You say no." |
| Gave Kyle the seat | "Kyle is at the front of the crowd when your door opens. He saved you a seat." |
| Took the seat | "Kyle lands an hour later. He walks past you without a word." |

A final Visko video shows the landing.

## Video

Each turn produces two Visko prompts written together as one continuous shot:

1. **Outcome:** what the choice just made caused.
2. **Scene:** the setup for the next decision. It opens on the image the outcome ended on and stays on screen while the choices appear.

When an emergency follows, the outcome ends leading into it, and the next scene opens with the player arriving at the next checkpoint.

Every prompt starts with the same style line and the checkpoint's fixed visual description. Prompts describe "the student" in third person, with no dialogue, no on-screen text and no real names, including "Stanford."

## How it runs

- **Unity (Quest)** shows the video, the text box and the 3D objects, and reports what the player grabbed.
- **The game server** (Python, FastAPI) holds the score and items, enforces the checkpoints, and asks the AI for each turn. Endpoints: `/start`, `/choose`, `/use_item`.
- **The AI** writes the narration, the two choices, their wellness scores and the two video prompts.
- **Visko** turns each prompt into video. Unity sends the prompts using the team's existing Visko script.

## Not yet in the build

The server was built before the latest design changes. It still needs:

- [ ] Distance, speed and the miles formula removed, and the verdict reduced to pass or fail at 85.
- [ ] Scoring changed to a flat +5 or -5, with the AI labelling each choice good or bad.
- [ ] The engine stall penalty changed from miles to wellness.
- [ ] The throw mini game at the Launch Site.
- [ ] The Deep Space checkpoint removed, so Moon Approach is checkpoint 5 and The Moon is checkpoint 6 with no decision.
- [ ] The fixed Kyle decision at Moon Approach and its four Kyle lines.
- [ ] The five archetypes and the end page with the full decision list.
- [ ] The tone changed from comedy to tense survival, and the fixed text rewritten to match.

## Still to decide

- [ ] Whether the throw replaces the Launch Site decision or happens alongside it.
- [ ] Whether the wellness number is shown during play or only the light, with the number revealed at the end.
- [ ] Whether players who fail also see their decision list.
- [ ] Whether the grab is 15 or 30 seconds. The server and early broadcast drafts say 30.
- [ ] The opening is at 2 a.m., but the intro clips show a setting sun. Change one, or say the sun no longer sets.
- [ ] Video style: cinematic and photorealistic for the intro, while the server's style line still says claymation.
- [ ] Whether to use the longer broadcast ("I repeat, this is not a test... Do not wait for assistance. None is coming.").
- [ ] Whether 85% is the right line, after a few test games.
- [ ] Project name. Candidates: Take Me to the Moon, Seating Is Limited, Last Launch, Three Items, 451.
