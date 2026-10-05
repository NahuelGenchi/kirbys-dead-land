# Host control tutorials

The original Dead Cells tutorials `jump`, `doubleJump`, `jumpDown`, `dodge1`, `dodge2`, and `homunculus` complete inside host main-skill callbacks. Kirby replaces those actions with the original DS controller, so those callbacks intentionally do not run. On a fresh profile, the first jump prompt would otherwise remain current and prevent later tutorials from starting.

`KirbyTutorials` rejects only those six prompts for the configured Kirby outfit and the active hero's own tutorial object. Activation cancels an already-current matching prompt through `Hero.cancelTuto`, which hides its `LightTip` and clears the current tutorial. Nothing is written to the tutorial's completed-progress map. Switching to another outfit restores the ordinary tutorial behavior. Weapon, interaction, healing, and other host prompts retain their original paths.

This is a prompt-lifecycle correction, not a movement unlock: the inspected original `Tutorial.tryToStart` only assigns the current ID, and `LightTip` creation/showing does not lock controls or pause gameplay. The completion sites were verified against the installed game's bytecode: `Jump.normalJump`, `AirJump.onStart`, `JumpDown.jumpDown`, `DodgeRoll.onStart`, and `Homunculus.onEnd`.
