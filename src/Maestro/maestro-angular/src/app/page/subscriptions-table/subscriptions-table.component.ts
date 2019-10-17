import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Subscription, Asset } from 'src/maestro-client/models';
import { Observable, of } from 'rxjs';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { map, shareReplay } from 'rxjs/operators';
import { StatefulResult, statefulPipe, statefulSwitchMap } from 'src/stateful';

class VersionDetails {

  // Dependency Records are <Name, Uri>
  public allDependencies: Record<string, string> = {};
  public missingDependencies: Record<string, string> = {};
  public missingDependenciesCount: Record<string, number> = {};
  public unusedSubscriptions: Record<string, string> = {};
  public conflictingSubscriptions: Record<string, Subscription[]> = {};

  constructor(inputFile: XMLDocument) {
    const productElements = inputFile.getElementsByTagName("ProductDependencies");
    let childProductElements = productElements[0].getElementsByTagName('Dependency');

    const toolsetElements = inputFile.getElementsByTagName("ToolsetDependencies");
    let childToolsetElements = productElements[0].getElementsByTagName('Dependency');

    for (let el of Array.from(childProductElements)) {
      this.parseDependencyElement(el, this.allDependencies);
    }

    for (let el of Array.from(childToolsetElements)) {
      this.parseDependencyElement(el, this.allDependencies);
    }
  }

  parseDependencyElement(element: Element, dependencyList: Record<string, string>) {
    let dependencyName: string;
    let dependencyUri: string;

    const attributesForElement = element.attributes;
    const nameAttribute = attributesForElement.getNamedItem("Name");

    if (nameAttribute) {
      dependencyName = nameAttribute.nodeValue || "UnknownAssetName";
    }
    else {
      dependencyName = "UnknownAssetName";
    }

    const uriAttribute = element.getElementsByTagName("Uri").item(0);

    if (uriAttribute) {
      dependencyUri = uriAttribute.textContent || "UnknownUri";
    }
    else {
      dependencyUri = "UnknownUri";
    }
    dependencyList[dependencyName] = dependencyUri;
  }

  getMissingSubscriptions(subscriptions: Subscription[]) {
    let missingSubs: Record<string, string> = {};
    let missingSubsCount: Record<string, number> = {};
    const sourceRepos = subscriptions.map((sub) => sub.sourceRepository);

    for (let key of Object.keys(this.allDependencies)) {
      if (!sourceRepos.includes(this.allDependencies[key])) {
        missingSubs[key] = this.allDependencies[key];
        missingSubsCount[this.allDependencies[key]] = (missingSubsCount[this.allDependencies[key]] || 0) + 1;
      }
    }

    this.missingDependencies = missingSubs;
    this.missingDependenciesCount = missingSubsCount;
  }

  getUnnecessarySubs(subscriptions: Subscription[]) {
    let extraSubs: Record<string, string> = {};
    const dependenciesUsed = Object.values(this.allDependencies);

    for (let sub of subscriptions) {
      if (sub.sourceRepository) {
        if (!dependenciesUsed.includes(sub.sourceRepository)) {
          extraSubs[sub.sourceRepository] = sub.sourceRepository;
        }
      }
    }

    this.unusedSubscriptions = extraSubs;
  }

  // Logic taken from SubscriptionHealthMetric in darc and adapted for this data source
  getConflictingSubs(subscriptions: Subscription[], assets: Asset[]) {

    let assetsToSub: Record<string, Subscription> = {};

    // Map asset to subscription the first time it's encountered, then add it to the conflicts list if it comes up again.
    for (let sub of subscriptions) {
      for (let asset of assets) {
        if (asset && asset.name && sub.sourceRepository) {
          const assetName = asset.name;
          const assetsToSubKeys = Object.keys(assetsToSub);

          if (assetsToSubKeys.includes(assetName)) {
            const otherSub = assetsToSub[assetName];

            if (otherSub.sourceRepository == sub.sourceRepository && (otherSub.channel && sub.channel && otherSub.channel.id == sub.channel.id)) {
              continue;
            }

            const conflictsKeys = Object.keys(this.conflictingSubscriptions);

            if (conflictsKeys.includes(assetName)) {
              this.conflictingSubscriptions[assetName].push(sub);
            }
            else {
              this.conflictingSubscriptions[assetName] = new Array();
              this.conflictingSubscriptions[assetName].push(sub);
              this.conflictingSubscriptions[assetName].push(otherSub);
            }
          }
          else {
            assetsToSub[assetName] = sub;
          }
        }
      }
    }
  }
}

@Component({
  selector: 'mc-subscriptions-table',
  templateUrl: './subscriptions-table.component.html',
  styleUrls: ['./subscriptions-table.component.scss']
})
export class SubscriptionsTableComponent implements OnChanges {

  @Input() public rootId?: number;
  @Input() public subscriptionsList?: Record<string, Subscription[]>;
  @Input() public includeSubToolsets?: boolean;
  @Input() public assets!: Asset[];
  public currentBranch?: string;
  public openDetails = false;
  public openAssets = false;
  public openDependencies = false;
  public versionDetailsList: Record<string, Observable<StatefulResult<VersionDetails>>> = {};

  get branches() {
    return Object.keys(this.subscriptionsList || {});
  }

  loadVersionData() {
    if (this.subscriptionsList) {
      for (const branch of this.branches) {
        const fileUrl = getDetailsFileUrl(this.subscriptionsList[branch][0].targetRepository || "", branch);

        const fileRequest = of(fileUrl).pipe(
          statefulSwitchMap(
            (uri) => {
              return this.http.get(uri, { responseType: 'text', headers: new HttpHeaders({ 'Accept': "text/plain" }) });
            }
          ),
          statefulPipe(
            map(
              (file) => {
                const xmlData = new DOMParser().parseFromString(file, "text/xml");
                return new VersionDetails(xmlData);
              }),
            map(
              (versionDetails) => {
                if (this.subscriptionsList) {
                  versionDetails.getMissingSubscriptions(this.subscriptionsList[branch]);
                  versionDetails.getUnnecessarySubs(this.subscriptionsList[branch]);
                  versionDetails.getConflictingSubs(this.subscriptionsList[branch], this.assets);
                }
                return versionDetails;
              }
            ),
            shareReplay(1)
          ));

        this.versionDetailsList[branch] = fileRequest;
      };
    }
  }

  constructor(private http: HttpClient) { }

  ngOnChanges(changes: SimpleChanges) {
    this.currentBranch = this.branches[0];
    if ("subscriptionsList" in changes) {
      this.loadVersionData();
    }
  }
}

function getDetailsFileUrl(sourceRepoStr: string, branchName: string) {
  if (sourceRepoStr.includes("github")) {
    return sourceRepoStr.replace("https://github.com", "https://raw.githubusercontent.com") + "/" + branchName + "/eng/Version.Details.xml";
  }
  else {
    const splitRepoUrl = sourceRepoStr.split('/');
    return `/_/AzDev/getFile/${splitRepoUrl[3]}/${splitRepoUrl[4]}/${splitRepoUrl[6]}/eng/Version.Details.xml`;
  }
}
