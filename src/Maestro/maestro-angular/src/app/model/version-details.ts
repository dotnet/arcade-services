import { Subscription, Asset, Build } from 'src/maestro-client/models';
import { Observable, combineLatest, of } from 'rxjs';
import { map, switchMap, shareReplay } from 'rxjs/operators';
import { StatefulResult, statefulPipe } from 'src/stateful';
import { BuildService } from 'src/app/services/build.service';
import { WrappedError, Loading, statefulSwitchMap } from 'src/stateful/helpers';
import { HttpHeaders, HttpClient } from '@angular/common/http';
import { DependencyDetail } from './dependency-detail';
import { SubscriptionDependencyDetails } from './subscription-dependency-details';

export class VersionDetails {

  // <DependencyName, DependencyDetails>
  public allDependencies: Record<string, DependencyDetail> = {};
  public dependenciesForEvaluation: Record<string, DependencyDetail> = {};

  // <SubId, Subscription>
  public unusedSubscriptions: Record<string, Subscription> = {};
  public conflictingSubscriptions: Record<string, Subscription[]> = {};
  public unableToRetrieveAssetsFor: Record<string, Subscription> = {};

  // Dependency Name
  public dependenciesWithNoSubscription: string[] = new Array();

  // Subscription ID
  public isToolsetSubscription: Record<string, boolean> = {};

  constructor(inputFile: XMLDocument) {
    const productElements = inputFile.getElementsByTagName("ProductDependencies");

    if (productElements == null || productElements.length != 0) {
      let childProductElements = productElements[0].getElementsByTagName('Dependency');

      for (let el of Array.from(childProductElements)) {
        const details: DependencyDetail = this.parseDependencyElement(el, false);
        if (details.name) {
          this.allDependencies[details.name] = details;
        }
      }
    }

    const toolsetElements = inputFile.getElementsByTagName("ToolsetDependencies");

    if (toolsetElements == null || toolsetElements.length != 0) {
      let childToolsetElements = toolsetElements[0].getElementsByTagName('Dependency');

      for (let el of Array.from(childToolsetElements)) {
        const details: DependencyDetail = this.parseDependencyElement(el, true);
        if (details.name) {
          this.allDependencies[details.name] = details;
        }
      }
    }

    const depNames = Object.keys(this.allDependencies);
    for (let dep of depNames) {
      if (!this.allDependencies[dep].pinned && !this.allDependencies[dep].coherentParentDependencyName) {
        this.dependenciesForEvaluation[dep] = this.allDependencies[dep];
      }
    }
  }

  // Takes an element from Version.Details.xml and turns it into a DependencyDetail
  parseDependencyElement(element: Element, toolset: boolean): DependencyDetail {
    let details = new DependencyDetail();

    details.fromToolset = toolset;

    const attributesForElement = element.attributes;
    const nameAttribute = attributesForElement.getNamedItem("Name");

    if (nameAttribute) {
      details.name = nameAttribute.nodeValue || "UnknownAssetName";
    }
    else {
      details.name = "UnknownAssetName";
    }

    const uriAttribute = element.getElementsByTagName("Uri").item(0);

    if (uriAttribute) {
      details.repoUrl = uriAttribute.textContent || "UnknownUri";
    }
    else {
      details.repoUrl = "UnknownUri";
    }

    const coherentParent = attributesForElement.getNamedItem("CoherentParentDependency");

    if (coherentParent) {
      details.coherentParentDependencyName = coherentParent.textContent || undefined;
    }

    const pinned = attributesForElement.getNamedItem("Pinned");

    if (pinned) {
      details.pinned = pinned.textContent === 'true';
    }

    return details;
  }

  // Determines if the given subscriptions have dependencies that are exclusively from the toolset category
  // Takes a collection of subscription + build asset objects and compares them against dependenciesForEvaluation
  // Returns a mapping of subscription id to boolean
  checkIfToolsetSubscriptions(subsAndAssets: Record<string, Asset[]>): Record<string, boolean> {
    // A build is a "toolset" if it has at least one dependency pointing to it,
    // and all dependencies pointing to it are toolset dependencies
    // Applying the same rules to subscriptions
    const subKeys = Object.keys(subsAndAssets);
    const depNames = Object.keys(this.dependenciesForEvaluation);

    // AssetName, SubId
    let flattenedData: Record<string, string> = {};

    // SubId, bool
    let isToolsetSub: Record<string, boolean> = {};

    // Flatten the subsAndAssets collection so that "includes" can cover all of the objects in it
    for (let subId in subsAndAssets) {
      for (let asset of subsAndAssets[subId]) {
        if (asset.name) {
          flattenedData[asset.name] = subId;
        }
      }
    }

    for (let sub of subKeys) {

      isToolsetSub[sub] = true;
      let foundDependency = false;

      for (let dep of depNames) {
        if (Object.keys(flattenedData).includes(dep) && flattenedData[dep] == sub) {

          foundDependency = true;

          if (!this.dependenciesForEvaluation[dep].fromToolset) {
            isToolsetSub[sub] = false;
          }
        }
      }

      // It's not a "toolset" if there are no dependencies that are pulled from it
      if (!foundDependency) {
        isToolsetSub[sub] = false;
      }
    }

    return isToolsetSub;
  }

  // Logic taken from SubscriptionHealthMetric in darc and adapted for this data source
  // Goes through each subscription given and determines if any are unnecessary or if there are any dependencies that don't come from a subscription
  // Returns collections for unusedSubscriptions(Record<string, Subscription>) and dependenciesWithNoSubscription (string[])
  getUnnecessaryAndMissingSubs(subsAndAssets: Record<string, Asset[]>, subscriptions: Subscription[]) {
    // Id, Subscription
    let extraSubs: Record<string, Subscription> = {};
    let missingSubs: string[] = new Array();

    const dependenciesUsed = Object.keys(this.dependenciesForEvaluation);
    const subKeys = Object.keys(subsAndAssets) || {};

    // AssetName, SubId
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

    return { extraSubs, missingSubs };
  }

  // Logic taken from SubscriptionHealthMetric in darc and adapted for this data source
  // Goes through each dependency and checks if there's more than one subscription that provides it
  getConflictingSubs(subsAndAssets: Record<string, Asset[]>, subscriptions: Subscription[]): Record<string, Subscription[]> {
    let assetsToSub: Record<string, Subscription> = {};
    let conflictingSubs: Record<string, Subscription[]> = {};

    // Map asset to subscription the first time it's encountered, then add it to the conflicts list if it comes up again.
    for (let sub of subscriptions) {
      if (subsAndAssets[sub.id]) {
        for (let asset of subsAndAssets[sub.id]) {
          if (asset && asset.name && sub.sourceRepository) {
            const assetName = asset.name;
            const assetsToSubKeys = Object.keys(assetsToSub);

            if (assetsToSubKeys.includes(assetName)) {
              const otherSub = assetsToSub[assetName];

              if (otherSub.sourceRepository == sub.sourceRepository && (otherSub.channel && sub.channel && otherSub.channel.id == sub.channel.id)) {
                continue;
              }

              const conflictsKeys = Object.keys(conflictingSubs);

              if (conflictsKeys.includes(assetName)) {
                conflictingSubs[assetName].push(sub);
              }
              else {
                conflictingSubs[assetName] = new Array();
                conflictingSubs[assetName].push(sub);
                conflictingSubs[assetName].push(otherSub);
              }
            }
            else {
              assetsToSub[assetName] = sub;
            }
          }
        }
      }
    }

    return conflictingSubs;
  }

  // Queries the Maestro build service to get the most recent assets for all dependent subscriptions
  getLatestAssetsForSubs(subscriptions: Subscription[], buildService: BuildService): Observable<StatefulResult<Record<string, Asset[]>>> {
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

        // Fail out if there were no successes
        let errors = subs.filter(s => s instanceof WrappedError) as WrappedError[];
        if (errors.length) {
          if (errors.length == subs.length) {
            return errors[0];
          }
        }

        // Return early to wait if things are still loading
        const loadings = subs.filter(s => s instanceof Loading);
        if (loadings.length) {
          return loadings[0];
        }

        // Log any error found while trying to get assets, don't fail the entire process because a subset of repos aren't returning assets
        for (let err of errors) {
          console.log(errors[0].error.message);
        }

        // Parse & store any good data that was returned
        let subsWithAssets: Record<string, Asset[]> = {};
        let successes = subs.filter(s => !(s instanceof WrappedError)) as [Subscription, Build][];
        for (const [sub, build] of successes as [Subscription, Build][]) {
          subsWithAssets[sub.id] = build.assets || [];
        }

        return subsWithAssets;
      }),
    ) as Observable<StatefulResult<Record<string, Asset[]>>>;

    return result;
  }

  static getDetailsFileUrl(sourceRepoStr: string, branchName: string): string {

    const splitRepoUrl = sourceRepoStr.split('/');

    if (sourceRepoStr.includes("github")) {
      return sourceRepoStr.replace("https://github.com", "https://raw.githubusercontent.com") + "/" + branchName + "/eng/Version.Details.xml";
    }
    else {
      const splitRepoUrl = sourceRepoStr.split('/');
      return `/_/AzDev/getFile/${splitRepoUrl[3]}/${splitRepoUrl[4]}/${splitRepoUrl[6]}`;
    }
  }

  static getDependencyDetailsUrl(sourceRepoStr: string, branchName: string): string {
    const splitRepoUrl = sourceRepoStr.split('/'); // 4 & 6, respectively to get the repo name

    if (sourceRepoStr.includes("github")) {
      return `/_/Dependencies/getSubscriptionDependencyDetails/github/maestro-auth-test/AspNetCore/master`;
     // return `/_/Dependencies/getSubscriptionDependencyDetails/github/${splitRepoUrl[3]}/${splitRepoUrl[4]}/${branchName}`;
    }
    else {
      return `/_/Dependencies/getSubscriptionDependencyDetails/azdev/${splitRepoUrl[3]}/${splitRepoUrl[6]}/${branchName}`;
    }
  }

  // Retrieves the Version.Details.xml file and uses it return a new VersionDetails with allDependencies and dependenciesForEvaluation filled out
  static retrieveAndParseFile(http: HttpClient, repository: string, branchName: string): Observable<StatefulResult<VersionDetails>> {

    const fileUrl = VersionDetails.getDetailsFileUrl(repository || "", branchName);
    const depUrl = VersionDetails.getDependencyDetailsUrl(repository || "", branchName);

    return of(depUrl).pipe(
      statefulSwitchMap(
        (uri) => {
        //  return http.get(uri, { responseType: 'text', headers: new HttpHeaders({ 'Accept': "text/plain" }) });
        return http.get(uri, {responseType: 'json', headers: new HttpHeaders({Accept: "text/json"})});
        }
      ),
      map<StatefulResult<string>, StatefulResult<string>>(
        (result) => {
          if (result instanceof WrappedError) {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?><Dependencies><ProductDependencies></ProductDependencies><ToolsetDependencies><ToolsetDependencies></Dependencies>";
          }
          return result;
        }
      ),
      statefulPipe(
        map(
          (file) => {
            const data = new SubscriptionDependencyDetails();
            const xmlData = new DOMParser().parseFromString(file, "text/xml");
            return new VersionDetails(xmlData);
          })));
  }

  // Retrieves the Version.Details.xml file and uses it return a new VersionDetails with all dependency and subscription lists filled out
  // Returns an Observable<StatefulResult<VersionDetails>>
  static retrieveAndParseSubscriptionData(subscriptionsList: Record<string, Subscription[]>, branch: string, buildService: BuildService, http: HttpClient) {
    return this.retrieveAndParseFile(http, subscriptionsList[branch][0].targetRepository || "", branch).pipe(
      statefulPipe(
        switchMap(
          (versionDetails) => {
            let newDetails: VersionDetails = versionDetails;
            if (subscriptionsList) {
              let assetsList = versionDetails.getLatestAssetsForSubs(subscriptionsList[branch], buildService);
              const updated = assetsList.pipe(
                statefulPipe(
                  map((assets) => {

                    let updatedDetails = versionDetails;

                    if (subscriptionsList) {
                      const processedSubs = updatedDetails.getUnnecessaryAndMissingSubs(assets, subscriptionsList[branch]);
                      updatedDetails.unusedSubscriptions = processedSubs.extraSubs;
                      updatedDetails.dependenciesWithNoSubscription = processedSubs.missingSubs;
                      const conflictingSubs = updatedDetails.getConflictingSubs(assets, subscriptionsList[branch]);
                      updatedDetails.conflictingSubscriptions = conflictingSubs;
                      updatedDetails.isToolsetSubscription = updatedDetails.checkIfToolsetSubscriptions(assets);

                      // Add errors for anything that didn't retrieve assets
                      const getAssetFailed: Record<string, Subscription> = {};
                      const assetKeys = Object.keys(assets);
                      for (let sub of subscriptionsList[branch]) {
                        if (!assetKeys.includes(sub.id)) {
                          getAssetFailed[sub.id] = sub;
                        }
                      }
                      updatedDetails.unableToRetrieveAssetsFor = getAssetFailed;
                    }
                    return updatedDetails;
                  }))
              );
              return updated;
            }
            return of(newDetails);
          }
        ),
        shareReplay(1)
      ));
  };
}
