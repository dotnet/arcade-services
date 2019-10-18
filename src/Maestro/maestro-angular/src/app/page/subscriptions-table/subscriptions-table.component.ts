import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Subscription, Asset, Build } from 'src/maestro-client/models';
import { Observable, of, combineLatest, ObjectUnsubscribedError } from 'rxjs';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { map, shareReplay, switchMap, filter } from 'rxjs/operators';
import { StatefulResult, statefulPipe, statefulSwitchMap } from 'src/stateful';
import { BuildService } from 'src/app/services/build.service';
import { WrappedError, Loading } from 'src/stateful/helpers';

class VersionDetails {

  // <DependencyName, Uri>
  public allDependencies: Record<string, string> = {};

  // <SubId, Subscription>
  public unusedSubscriptions: Record<string, Subscription> = {};
  public conflictingSubscriptions: Record<string, Subscription[]> = {};

  // Dependency Name
  public dependenciesWithNoSubscription: string[] = new Array();

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

  // Logic taken from SubscriptionHealthMetric in darc and adapted for this data source
  getUnnecessaryAndMissingSubs(subsAndAssets: Record<string, Asset[]>, subscriptions: Subscription[]) {
    // Id, Subscription
    let extraSubs: Record<string, Subscription> = {};
    let missingSubs: string[] = new Array();

    const dependenciesUsed = Object.keys(this.allDependencies);
    const subKeys = Object.keys(subsAndAssets) || {};

    //AssetName, SubId
    let flattenedData: Record<string, string> = {};

    // Populate the extraSubs collection, then subscriptions will be deleted from it as their use is confirmed
    for (let sub of subscriptions) {
      if (subKeys.includes(sub.id)) {
        extraSubs[sub.id] = sub;
      }
    }

    // Flatten the subsAndAssets collection so that "includes" can cover all of the objects in it
    for (let subId in subsAndAssets) {
      for (let asset of subsAndAssets[subId]) {
        if (asset.name) {
          flattenedData[asset.name] = subId;
        }
      }
    }

    // Compare the list of assets used against the assets produced by each sub
    for (let dep of dependenciesUsed) {
      if (flattenedData) {

        // If the dependency can be found in a subscription then that subscription is used
        if (Object.keys(flattenedData).includes(dep)) {
          delete extraSubs[flattenedData[dep]];
        }
        else {
          // If the dependency name isn't in the list of assets created by any subscription then the subscription for that dependency is missing
          missingSubs.push(dep);
        }
      }
    }

    this.unusedSubscriptions = extraSubs;
    this.dependenciesWithNoSubscription = missingSubs;
  }

  // Logic taken from SubscriptionHealthMetric in darc and adapted for this data source
  getConflictingSubs(subsAndAssets: Record<string, Asset[]>, subscriptions: Subscription[]) {

    let assetsToSub: Record<string, Subscription> = {};

    // Map asset to subscription the first time it's encountered, then add it to the conflicts list if it comes up again.
    for (let sub of subscriptions) {
      for (let asset of subsAndAssets[sub.id]) {
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

  getAndProcessLatestAssetsForSubs(subscriptions: Subscription[], buildService: BuildService) {
    // Can't make a Record with <Subscription, string[]> so use the subscriptionId as a proxy
    const subWithBuilds: Observable<StatefulResult<[Subscription | null, Build | null]>[]> = <any>combineLatest.apply(undefined, subscriptions.filter(s => s.channel && s.sourceRepository).map(sub => {
      const buildId = buildService.getLatestBuildId(sub.channel!.id, sub.sourceRepository!);
      return buildId.pipe(
        statefulPipe(
          switchMap(id => buildService.getBuild(id)),
        ),
        statefulPipe(
          map(build => [sub, build]),
        ));
    }));
    const result = subWithBuilds.pipe(
      map(subs => {
        const errors = subs.filter(s => s instanceof WrappedError);
        if (errors.length) {
          return errors[0];
        }

        const loadings = subs.filter(s => s instanceof Loading);
        if (loadings.length) {
          return loadings[0];
        }

        let subsWithAssets: Record<string, Asset[]> = {};
        for (const [sub, build] of subs as [Subscription, Build][]) {
          subsWithAssets[sub.id] = build.assets || [];
        }

        this.getUnnecessaryAndMissingSubs(subsWithAssets, subscriptions);
        this.getConflictingSubs(subsWithAssets, subscriptions);

        return subsWithAssets;
      }),
    ) as Observable<StatefulResult<Record<string, Asset[]>>>;
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
                  versionDetails.getAndProcessLatestAssetsForSubs(this.subscriptionsList[branch], this.buildService);
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

  constructor(private http: HttpClient, private buildService: BuildService) { }

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
