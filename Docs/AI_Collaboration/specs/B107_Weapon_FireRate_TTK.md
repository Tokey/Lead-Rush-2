# B107: Distinct fire cadence with TTK preserved

## Change

The five shipped weapons now have slower, distinct rates: MK18 700 RPM, AK-12 600, AK-74N 550, FAL 500, and MK23 300. The MK23 is semi-automatic, so 300 RPM is its game-side repeat limit rather than a mechanical cyclic rate. The rifle rates are deliberately tuned below the previous game settings for the slower feel the developer requested; they are gameplay values, not claims about each weapon's measured cyclic rate.

The `near / mid / far` body-damage steps changed as follows:

| Weapon | RPM before -> after | Damage before -> after |
|---|---:|---:|
| MK18 | 850 -> 700 | 32 / 26 / 21 -> 37.5 / 33.4 / 21.5 |
| AK-12 | 700 -> 600 | 35 / 29 / 24 -> 37.5 / 30 / 25 |
| AK-74N | 650 -> 550 | 33 / 28 / 23 -> 37.5 / 30 / 25 |
| FAL | 600 -> 500 | 48 / 42 / 36 -> 48 / 42 / 37.5 |
| MK23 | 400 -> 300 | 30 / 25 / 18 -> 37.5 / 33.4 / 21.5 |

## TTK calculation

TTK is counted from the first shot at time zero: `(ceil(HP / body damage) - 1) * 60 / RPM`. The table shows near/mid/far milliseconds for full-health body hits, once at 100 HP and once at the game's current 150 effective HP (100 health + 50 armor). Values are rounded to the nearest millisecond.

| Weapon | 100 HP before -> after | 150 effective HP before -> after |
|---|---|---|
| MK18 | 212 / 212 / 282 -> 171 / 171 / 343 ms | 282 / 353 / 494 -> 257 / 343 / 514 ms |
| AK-12 | 171 / 257 / 343 -> 200 / 300 / 300 ms | 343 / 429 / 514 -> 300 / 400 / 500 ms |
| AK-74N | 277 / 277 / 369 -> 218 / 327 / 327 ms | 369 / 462 / 554 -> 327 / 436 / 545 ms |
| FAL | 200 / 200 / 200 -> 240 / 240 / 240 ms | 300 / 300 / 400 -> 360 / 360 / 360 ms |
| MK23 | 450 / 450 / 750 -> 400 / 400 / 800 ms | 600 / 750 / 1200 -> 600 / 800 / 1200 ms |

Because shots arrive in whole intervals, a slower rate cannot exactly preserve every TTK tier at both 100 and 150 HP. Damage was set to the closest body-hit threshold across both health totals, keeping each change within one shot interval of the prior TTK.

## Reference rates

- The US Army M4/M4A1 technical manual lists a 700-970 RPM cyclic range; the MK18 target uses its low end. [Army TM 9-1005-319-10](https://www.gpo.gov/docs/default-source/contracts/tm-9-1005-319-10-rev.pdf)
- Rostec lists the AK-12 at up to 700 RPM. [Rostec, AK-12 technical overview](https://rostec.ru/en/media/news/the-latest-kalashnikov-assault-rifles-were-demonstrated-at-the-victory-parade/)
- A U.S. Marine Corps reference lists the AK-74 at 600 RPM. [MCWP 3-11.1](https://www.marines.mil/Portals/1/MCWP%203-11.1.pdf)
- The FN FAL manual lists 650-700 RPM cyclic. [FN FAL manual](https://www.forgottenweapons.com/wp-content/uploads/manuals/FN-FAL%20Manual.pdf)
- Heckler & Koch specifies the MK23 as semi-automatic, with no cyclic RPM. [HK Mark 23 product flyer](https://s3.amazonaws.com/hk-manuals/files/Catalogs/Product_Flyers/HK_Mark23_Flyer-1.pdf)

The rates in LR2 intentionally sit below these automatic-weapon mechanical rates to address the developer's play-feel report. The sources are reference points; the gameplay damage and cadence are tuned together.
