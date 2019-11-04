import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Subscription, Asset } from 'src/maestro-client/models';
import { Observable, of } from 'rxjs';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { map, shareReplay, switchMap } from 'rxjs/operators';
import { StatefulResult, statefulPipe, statefulSwitchMap } from 'src/stateful';
import { BuildService } from 'src/app/services/build.service';
import { WrappedError } from 'src/stateful/helpers';
import { VersionDetails } from 'src/app/model/version-details';

@Component({
  selector: 'mc-subscriptions-table',
  templateUrl: './subscriptions-table.component.html',
  styleUrls: ['./subscriptions-table.component.scss']
})
export class SubscriptionsTableComponent implements OnChanges {

  @Input() public rootId?: number;
  @Input() public subscriptionsList?: Record<string, Subscription[]>;
  @Input() public assets!: Asset[];

  public currentBranch?: string;
  public openDetails = false;
  public openAssets = false;
  public openDependencies = false;
  public versionDetailsList: Record<string, Observable<StatefulResult<VersionDetails>>> = {};

  get branches() {
    return Object.keys(this.subscriptionsList || {});
  }

  // Retrieves dependencies from the Version.Details.xml file for the current repo
  // Then gets assets for its subscriptions and cross references them with the dependencies to get the current subscription usage state
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
                const xmlData = new DOMParser().parseFromString(file, "text/xml");
                return new VersionDetails(xmlData);
              }),
            switchMap(
              (versionDetails) => {
                let newDetails: VersionDetails = versionDetails;
                if (this.subscriptionsList) {
                  let assetsList = versionDetails.getLatestAssetsForSubs(this.subscriptionsList[branch], this.buildService);
                  const updated = assetsList.pipe(
                    statefulPipe(
                      map((assets) => {

                        let updatedDetails = versionDetails;

                        if (this.subscriptionsList) {
                          const processedSubs = updatedDetails.getUnnecessaryAndMissingSubs(assets, this.subscriptionsList[branch]);
                          updatedDetails.unusedSubscriptions = processedSubs.extraSubs;
                          updatedDetails.dependenciesWithNoSubscription = processedSubs.missingSubs;
                          const conflictingSubs = updatedDetails.getConflictingSubs(assets, this.subscriptionsList[branch]);
                          updatedDetails.conflictingSubscriptions = conflictingSubs;
                          updatedDetails.isToolsetSubscription = updatedDetails.checkIfToolsetSubscriptions(assets);

                          // Add errors for anything that didn't retrieve assets
                          const getAssetFailed: Subscription[] = new Array();
                          const assetKeys = Object.keys(assets);
                          for (let sub of this.subscriptionsList[branch]) {
                            if (!assetKeys.includes(sub.id)) {
                              getAssetFailed.push(sub);
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
