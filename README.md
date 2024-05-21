This repository aims providing 3D Stereo Support for the [PRo3D ecosystem](https://pro3d.space/) in particular [PRo3D](https://github.com/pro3d-space/PRo3D).
Currently, the repository only contains a stereographic 3D Viewer for the Ordered Point Cloud Format (OPC) based on 
the [aardvark-platform](https://aardvarkians.com/) which could serve as a basis for further development leveraging 3D data on 3D hardware.

Rendering works on PRO graphics with quad-buffer stereo, tested on [PluraView](https://www.3d-pluraview.com/de/)
![alt text](docs/dino.png)
![alt text](docs/mola.png)

How to run:
- Download the [Release](https://github.com/pro3d-space/PRo3D.Stereo/releases/tag/v0.0.1)
- Download a dataset, e.g. from [PRo3D.Space](https://pro3d.space/), e.g. victoria crater http://download.vrvis.at/acquisition/32987e2792e0/PRo3D/VictoriaCrater.zip (this is a http url, you need to download by copying into address bar...) and unzip to `VictoriaCrater` beside the executable.
- Run the `PRo3D.Stereo.exe VictoriaCrater\HiRISE_VictoriaCrater_SuperResolution` in cmd. 