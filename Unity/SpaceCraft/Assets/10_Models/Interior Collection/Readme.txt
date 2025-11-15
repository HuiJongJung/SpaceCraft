Thanks for buying our asset!

- If something looks wrong, try to download the post processing asset (for Built-In), just go to Window -> Package Manager and download "Post processing". After the import check the "PostProcessVolume" game object in the scene. You can assign postprocess file there and made any changes.

- You can use our ColorMask shaders to customize some objects colors or switch it to Standard Shader.

- If lighting looks broken, do not worry, it's usual thing after import asset in a different versions of the Unity. You can easily fix it: just rebake lightmap.

Open: Window -> Rendering -> Light Settings and click "Generate Lighting". After few minutes all will be done and lighting will looks fine. 

Remember that Progressive render need a powerful CPU or GPU. If your PC is not so good, you can reduce some settings, like "Samples", "Lightmap Resolution" and "Lightmap Size". Check Unity documentation for more info. Remember that GPU is MUCH faster.

- If you want to use asset in URP/HDRP instead of Built-In pipeline, just switch project to it and import the corresponding package from the "Extras" folder (remember that current Built-In Postprocessing will not work in other pipelines so you should setup it on your side if necessary. For HDRP you also should setup lighting, check the unity documentation by link below).

Important note! Due Unity bug color ID shader can may still remain pink in HDRP, what means it works incorrect. Just open Shaders folder in Explorer and remove ColorMaskMetallic.shadergraph.meta and ColorMaskSpecular.shadergraph.meta files, it should reimport shaders and fix the issue.

Upgrading documentation for URP:
https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@7.1/manual/InstallURPIntoAProject.html

Upgrading documentation for HDRP:
https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@14.0/manual/Upgrading-To-HDRP.html

-----------------------------------------------------------------

If you have any questions, wishes or suggestions just contact us:

	- Discord server: https://discord.gg/Vn5mW7z
	- Email: hello@vxstudio.ru