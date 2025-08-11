function Get-ReleaseNotes {
  param([xml]$csProj)
  $b = [System.Text.StringBuilder]::new()  
  $a = $csProj.
    SelectSingleNode("//PackageReleaseNotes").
    InnerText.
    Trim().
    Split([Environment]::NewLine) | ForEach-Object {
      $b.AppendLine($_.Trim())
    }
  return $b.ToString()
}

Write-Output "Batak, ktery vytvori baliky pro NUGET."
Write-Output "Baliky by mely mit nastavenou verzi a popisky zmen ve svych *.csproj"
Write-Output "Budou se pakovat projekty:"
Write-Output "- Hangfire.Storage.MySql"
Write-Output ""
Write-Output "Ve vsech *.csproj je treba nastavit:"
Write-Output "  - Spravnou verzi"
Write-Output "  - PackageReleaseNotes"

Read-Host "Press Enter to continue"

$csprojFile = "..\src\Hangfire.Storage.MySql\Hangfire.Storage.MySql.csproj"

[xml]$csprojXml = Get-Content -Path $csprojFile
$version = ($csprojXml.Project.PropertyGroup.Version | Out-string).Trim()
##$releaseNotes = ($csprojXml.Project.PropertyGroup.PackageReleaseNotes | Out-string).Trim().Split([Environment]::NewLine) `
 # | foreach{ $_.Trim()}
#$b = [System.Text.StringBuilder]::new()
#$a = $csprojXml.
#  SelectSingleNode("//PackageReleaseNotes").
#  InnerText.Trim().
#  Split([Environment]::NewLine) | ForEach-Object {
#    $b.AppendLine($_.Trim())
#  }
$releaseNotes = Get-ReleaseNotes -csProj $csprojXml
  
$packageId = ($csprojXml.Project.PropertyGroup.PackageId | Out-string).Trim()

#$version = dotnet pack $csprojFile -getProperty:Version
#$releaseNotes = dotnet pack $csprojFile -getProperty:PackageReleaseNotes
#$packageId = dotnet pack $csprojFile -getProperty:PackageId
$packageFileName = "$packageId.$version.nupkg"
$packageDir = ".\packages\" # has to end with back-slash
$packageFile = "$packageDir$packageFileName"
$tag = "v$version"

#Write-Output $version 
#Write-Output $csprojXml.GetType()
#Write-Output $a
#Write-Output $a.GetType()
#Write-Output $b.ToString()
#Write-Output $a.GetType()
#Write-Output $releaseNotes
#Write-Output $packageId
#Write-Output $packageFileName
#Write-Output $packageFile

dotnet pack $csprojFile -c Release -o $packageDir


Read-Host -Prompt "Bude vytvoren tag $tag a novy release"

git add "$csprojFile"
git commit -m "Package $version"
git push origin

git tag -a "$tag" -m "$releaseNotes"
git push origin tag "$tag"

gh release create "$tag" "$packageFile" --title "$packageId $version" --notes-from-tag

Write-Host ""
Write-Host ""
Write-Host ""
#ECHO Vytvoreni baliku probehlo.
#ECHO Nyni budou publikovany na OcelovyNuget. 
#ECHO Ted je chvile je zkontrolovat (je spravne nastavena verze?, jsou zde pouze baliky, ktere bud publikovany?).
#ECHO Pokud je neco spatne, tak CTRL + C pro zruseni, jinak pokracovat libovolnou klavesou.
#PAUSE
nuget push "$packageFile"


#ECHO .
#ECHO .
#ECHO .
#ECHO Nyni Budou smazany nepotrebne *.nupkg baliky
#ECHO To je treba udelat, protoze pri publikace jsou publikovany vsechny baliky z tohoto adresare
#PAUSE
#del *.nupkg
#ECHO Smazano.
#PAUSE

