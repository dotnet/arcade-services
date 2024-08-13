const path = require("path");
const fs = require("fs");
const mkdirp = require("mkdirp");

const outDir = path.join(__dirname, "dist/maestro-angular");
const node_modules = path.join(__dirname, "node_modules");
const statsJson = path.join(outDir, "stats.json");
const assetsJson = path.join(outDir, "assets.json");

console.log(`READ ${statsJson}`);
const stats = JSON.parse(fs.readFileSync(statsJson));


const assetOrder = [
  "runtime",
  "es2015-polyfills",
  "polyfills",
  "vendor",
  "main",
];

console.log(`WRITE ${assetsJson}`);
const assets = {
  "scripts": [],
  "styles": [],
};
function addAsset(assetName, fileName) {
  if (fileName.endsWith(".js")) {
    assets.scripts.push({
      name: assetName,
      file: fileName,
    });
  } else {
    assets.styles.push({
      name: assetName,
      file: fileName,
    });
  }
}
function getAsset(name) {
  const assets = stats.assetsByChunkName[name];
  if (Array.isArray(assets)) {
    return assets[0];
  } else {
    return assets;
  }
}

for (const assetName of assetOrder) {
  const fileName = getAsset(assetName);
  delete stats.assetsByChunkName[assetName];
  addAsset(assetName, fileName);
}
for (const asset of Object.keys(stats.assetsByChunkName)) {
  const fileName = getAsset(asset);
  addAsset(asset, fileName);
}
fs.writeFileSync(assetsJson, JSON.stringify(assets, undefined, 2));

// Libraries which are included in pages directly and not part of the bundle
// We copy them manually to the output directory
const filesToCopy = [
  "bootstrap/dist/css/bootstrap.min.css",
  "bootstrap/dist/css/bootstrap-reboot.min.css",
  "bootstrap/dist/js/bootstrap.min.js",
  "d3/dist/d3.min.js",
  "popper.js/dist/umd/popper.min.js",
];

for (const file of filesToCopy) {
  console.log(`Copying ${file}`);
  mkdirp.sync(path.join(outDir, "libs", path.dirname(file)));
  fs.copyFileSync(
    path.join(node_modules, file),
    path.join(outDir, "libs", file));
}
