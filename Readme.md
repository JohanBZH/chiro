Nomenclature
Extension,Purpose,Type
.shp,Geometry (Coordinates),Mandatory
.shx,Geometric Index,Mandatory
.dbf,Attributes (The Table),Mandatory
.prj,"Map Projection (WGS84, etc.)",Recommended
.qml,Visual Styling (Colors/Icons),QGIS Only
.qix,Rendering Speedup,QGIS/GDAL

To publish a linux executable, run :
dotnet publish ./Chiro.App/Chiro.App.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish_output_linux

To publish a windows executable, run :
./publish.sh
