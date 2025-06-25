# Mountain Tweaks
Tiny mod to mess with some technical stuff.

## Current Tweaks
- Disable losing fullscreen when the window loses focus on X11 (also Xwayland): some window managers may not handle this properly, use with caution.
- Dumps all the DMDs compiled after this mod was loaded to a directory (Note: you can make this mod load earlier by renaming its zip file to something like `000MountainTweaks.zip`)
- Makes the method `SpriteBatch.PushSprite` from FNA not elegible for inlining: this fixes some mods that hook it (for example: Motion Smoothing)

It is not recommended to use any of these features if you do not understand what they do.

Some features requre some special handling that can only happen during the game's startup, consequently they present themselves as two toggles: one that enables the required setup on boot, and one that enables the feature at runtime. 
