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
mkdirp.sync(path.join(outDir, "libs/bootstrap/dist/css"));
mkdirp.sync(path.join(outDir, "libs/bootstrap/dist/js"));
mkdirp.sync(path.join(outDir, "libs/d3/dist"));
mkdirp.sync(path.join(outDir, "libs/popper.js/dist/umd"));

fs.copyFile(
  path.join(node_modules, "bootstrap/dist/css/bootstrap.min.css"),
  path.join(outDir, "libs/bootstrap/dist/css/bootstrap.min.css"),
  (err) => {
    if (err) throw err;
  });

fs.copyFile(
  path.join(node_modules, "bootstrap/dist/css/bootstrap-reboot.min.css"),
  path.join(outDir, "libs/bootstrap/dist/css/bootstrap-reboot.min.css"),
  (err) => {
    if (err) throw err;
  });

fs.copyFile(
  path.join(node_modules, "bootstrap/dist/js/bootstrap.min.js"),
  path.join(outDir, "libs/bootstrap/dist/js/bootstrap.min.js"),
  (err) => {
    if (err) throw err;
  });

fs.copyFile(
  path.join(node_modules, "d3/dist/d3.min.js"),
  path.join(outDir, "libs/d3/dist/d3.min.js"),
  (err) => {
    if (err) throw err;
  });

fs.copyFile(
  path.join(node_modules, "popper.js/dist/umd/popper.min.js"),
  path.join(outDir, "libs/popper.js/dist/umd/popper.min.js"),
  (err) => {
    if (err) throw err;
  });
