import { Subscription } from 'src/maestro-client/models';
import { DependencyDetail } from './dependency-detail';
import { SubscriptionConflict } from './subscription-conflict';
import { Observable, of } from 'rxjs';
import { map, switchMap, shareReplay } from 'rxjs/operators';
import { StatefulResult, statefulPipe } from 'src/stateful';
import { WrappedError, statefulSwitchMap, Loading } from 'src/stateful/helpers';
import { HttpHeaders, HttpClient } from '@angular/common/http';

export class SubscriptionDependencyDetails {
  repository?: string;
  branch?: string;
  subscriptions?: Array<Subscription>;
  dependencies?: Array<DependencyDetail>;
  conflictingSubscriptions?: Array<SubscriptionConflict>;
  dependenciesMissingSubscriptions?: Array<DependencyDetail>;
  dependenciesThatDoNotFlow?: Array<DependencyDetail>;
  unusedSubscriptions?: Array<Subscription>;

  unusedSubsMap: Record<string, Subscription> = {};
  conflictingSubsMap: Record<string, SubscriptionConflict> = {};
  //isToolsetSub: Record<string, boolean> = {};

  createSubMaps() {
    let unused: Record<string, Subscription> = {};
    if (this.unusedSubscriptions) {
      for (let sub of this.unusedSubscriptions) {
        if (sub.id) {
          unused[sub.id] = sub;
        }
      }
    }

    let conflicting: Record<string, SubscriptionConflict> = {};
    if (this.conflictingSubscriptions) {
      for (let sub of this.conflictingSubscriptions) {
        if (sub.Asset) {
          conflicting[sub.Asset] = sub;
        }
      }
    }

     return {unused, conflicting};
  }

  // Calls the DarcLib SubscriptionHealthMetric logic
  static retrieveDataFromServer(http: HttpClient, repository: string, branchName: string): Observable<StatefulResult<SubscriptionDependencyDetails>> {

    const depUrl = SubscriptionDependencyDetails.getDependencyDetailsUrl(repository || "", branchName);

    return of(depUrl).pipe(
      statefulSwitchMap(
        (uri) => {
          return http.get(uri, { responseType: 'json', headers: new HttpHeaders({ Accept: "text/json" }) });
        }
      ),
      map<StatefulResult<string>, StatefulResult<string>>(
        (result) => {
          if (result instanceof WrappedError) {
            return new SubscriptionDependencyDetails();
          }

          if(!(result instanceof Loading)){
            let subDetails: SubscriptionDependencyDetails = Object.assign(new SubscriptionDependencyDetails(), result);
            let subMaps = subDetails.createSubMaps();
            subDetails.unusedSubsMap = subMaps.unused;
            subDetails.conflictingSubsMap = subMaps.conflicting;
            return subDetails;
          }

          return result;

          // let details = new SubscriptionDependencyDetails();
          // //details.branch = result.
          // details.createSubMaps();
          // return details;
        }
      ),
      // statefulPipe(
      //   map(
      //     (file) => {
      //       const data = new SubscriptionDependencyDetails();
      //       const xmlData = new DOMParser().parseFromString(file, "text/xml");
      //       return new VersionDetails(xmlData);
      //     }))
      shareReplay(1)
    );
  }

  static getDependencyDetailsUrl(sourceRepoStr: string, branchName: string): string {
    const splitRepoUrl = sourceRepoStr.split('/'); // 4 & 6, respectively to get the repo name

    if (sourceRepoStr.includes("github")) {

      return `/_/Dependencies/getSubscriptionDependencyDetails/github/maestro-auth-test/AspNetCore/master`;
      //return `/_/Dependencies/getSubscriptionDependencyDetails/github/${splitRepoUrl[3]}/${splitRepoUrl[4]}/${branchName}`;
    }
    else {
      return `/_/Dependencies/getSubscriptionDependencyDetails/azdev/${splitRepoUrl[3]}/${splitRepoUrl[6]}/${branchName}`;
    }
  }

  // // Determines if the given subscriptions have dependencies that are exclusively from the toolset category
  // // Takes a collection of subscription + build asset objects and compares them against dependenciesForEvaluation
  // // Returns a mapping of subscription id to boolean
  // checkIfToolsetSubscriptions(subsAndAssets: Record<string, Asset[]>): Record<string, boolean> {
  //   // A build is a "toolset" if it has at least one dependency pointing to it,
  //   // and all dependencies pointing to it are toolset dependencies
  //   // Applying the same rules to subscriptions
  //   const subKeys = Object.keys(subsAndAssets);
  //   const depNames = Object.keys(this.dependencies || {});

  //   // AssetName, SubId
  //   let flattenedData: Record<string, string> = {};

  //   // SubId, bool
  //   let isToolsetSub: Record<string, boolean> = {};

  //   // Flatten the subsAndAssets collection so that "includes" can cover all of the objects in it
  //   for (let subId in subsAndAssets) {
  //     for (let asset of subsAndAssets[subId]) {
  //       if (asset.name) {
  //         flattenedData[asset.name] = subId;
  //       }
  //     }
  //   }

  //   for (let sub of subKeys) {

  //     isToolsetSub[sub] = true;
  //     let foundDependency = false;

  //     for (let dep of depNames) {
  //       if (Object.keys(flattenedData).includes(dep) && flattenedData[dep] == sub) {

  //         foundDependency = true;

  //         if (!this.dependencies[dep].fromToolset) {
  //           isToolsetSub[sub] = false;
  //         }
  //       }
  //     }

  //     // It's not a "toolset" if there are no dependencies that are pulled from it
  //     if (!foundDependency) {
  //       isToolsetSub[sub] = false;
  //     }
  //   }

  //   return isToolsetSub;
  // }
}
