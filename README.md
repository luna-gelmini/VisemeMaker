# VisemeMaker

Makes VRChat's 14 visemes from the vowel shapes most models already have (A, I, U, E, O) and sets
up lip sync on the avatar descriptor. Handy for MMD, VRoid, Source and other models that come
without VRChat visemes.

## Requirements

- Unity 2022.3 with the VRChat Avatars SDK 3.7 or newer

## Install

- **VRChat Creator Companion (recommended):** open https://vpm.ladyluh.dev/ and press
  **Add to VCC** (or add `https://vpm.ladyluh.dev/index.json` under Settings →
  Packages), then add **VisemeMaker** to your project.
- **.unitypackage:** download `VisemeMaker-<version>.unitypackage` from
  [Releases](https://github.com/luna-gelmini/VisemeMaker/releases) and drag it into Unity.
- **Git URL:** Window → Package Manager → **+** → *Add package from git URL* →
  `https://github.com/luna-gelmini/VisemeMaker.git`

## Use

1. Open **Tools > Viseme Maker** and pick the avatar. The face mesh is picked for you.
2. Check the five vowel shapes. They're detected from the shape names (`a`, `Ah`, `mouth_a`,
   `Fcl_MTH_A`, `あ` and so on); change any that are wrong.
3. Use **Preview** to look at each viseme on the avatar and adjust **Strength**.
4. Press **Create visemes**.

The visemes are added as `vrc.v_aa`, `vrc.v_oh` and so on to a copy of the face mesh, saved next to
the model as `<mesh>_visemes.asset`. The avatar descriptor's lip sync is set to use them. Running it
again replaces the old visemes instead of adding more.

## How the visemes are made

Each viseme is a weighted mix of the vowel shapes, for example `DD` is mostly I with some A, and
`oh` is O. A and O are required, plus I or E. With U and E as well, `ou` and `E` use their own
shapes; without them they're mixed from the others. `sil` stays empty, which is what VRChat
expects.

## License

MIT
