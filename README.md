# Stylized Art Pipeline for Unity

Contact: [Fernando Ramallo<fernando.ramallo@gmail.com>](mailto:fernando.ramallo@gmail.com)  
Website: [https://byfernando.com/](https://byfernando.com/)

This is an implementation and case study for a custom stylized art pipeline that includes:

- An asset importer and processor that applies shaders and metadata to 3D Models
- A custom ubershader with a full custom recoloring, fog, lighting solution
- Editor tools for assigning color palettes to models interactively
- A color palette system with interchangeable palette assets for different looks
- No need for custom materials. A single drawcall renders the entire scene.

Requirements: Tested on Unity 6000.3.11f1 URP

## Please see the full writeup and documentation at:

[https://app.notion.com/p/Fernando-Ramallo-Technical-Art-Case-Study-A-stylized-Art-Pipeline-39d985971dce80da932ef7ef8aaebf68?source=copy_link](https://app.notion.com/p/Fernando-Ramallo-Technical-Art-Case-Study-A-stylized-Art-Pipeline-39d985971dce80da932ef7ef8aaebf68?source=copy_link)


### tl;dr

Drag a model to the project, a custom importer applies the custom pipeline automatically. Use an in-editor tool to assign palette indices to meshes. Create Palette assets for specific looks. No need for multiple materials: an ubershader handles the entire look. The entire shading is artist controlled.

### Cycling through Palette objects

![Cycling through Palette objects](.assets/choosing.gif)

### Editing a Palette’s shader lighting

![Editing a Palette's shader lighting](.assets/shader.gif)

### Editing a Palette

![Editing a Palette](.assets/tweaking.gif)

### Uber shader

![Uber Shader](.assets/shader.png)

## Screenshots

![Main Camera - Colorful](.assets/cameras/Main_Camera__Corner_Colorful.asset.jpg)
![Main Camera - Desert](.assets/cameras/Main_Camera__Corner_Desert.asset.jpg)
![Main Camera - Night](.assets/cameras/Main_Camera__Corner_Night_1.asset.jpg)
![Main Camera (2) - Corner Night](.assets/cameras/Main_Camera_%282%29__Corner_Night.asset.jpg)
![Main Camera (2) - Corner Night variant](.assets/cameras/Main_Camera_%282%29__Corner_Night_1.asset.jpg)
![Main Camera (2) - Debug Palette](.assets/cameras/Main_Camera_%282%29__Game_DebugPalette.asset.jpg)
![Main Camera (3) - Desert](.assets/cameras/Main_Camera_%283%29__Corner_Desert.asset.jpg)
![Main Camera (3) - Empty](.assets/cameras/Main_Camera_%283%29__Corner_Empty.asset.jpg)
![Main Camera (3) - Night](.assets/cameras/Main_Camera_%283%29__Corner_Night_1.asset.jpg)
![Main Camera (6) - Foggy](.assets/cameras/Main_Camera_%286%29__Corner_Foggy.asset.jpg)
![Main Camera (6) - Night](.assets/cameras/Main_Camera_%286%29__Corner_Night.asset.jpg)
![Main Camera (6) - Night variant](.assets/cameras/Main_Camera_%286%29__Corner_Night_1.asset.jpg)

## Acknowledgments

 - [Amazon Lumberyard Bistro scene](https://developer.nvidia.com/orca/amazon-lumberyard-bistro), used under [Creative Commons CC-BY](https://creativecommons.org/licenses/by/4.0/) license.